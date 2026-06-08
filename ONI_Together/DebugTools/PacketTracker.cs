using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ImGuiNET;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using Steamworks;
using UnityEngine;

namespace ONI_Together.DebugTools
{
    public class PacketTracker
    {
        private static PacketTracker _instance;
        public int _nextTrackId;
        private bool showWindow = false;

        private string outgoing_filter = string.Empty;
        private string incoming_filter = string.Empty;

        public struct PacketTrackData
        {
            public IPacket packet;
            public int size;
            public int TrackId;
            public float Timestamp;
        }

        private const int BUFFER_CAPACITY = 50000;
        private PacketTrackData[] _incomingBuf = new PacketTrackData[BUFFER_CAPACITY];
        private PacketTrackData[] _outgoingBuf = new PacketTrackData[BUFFER_CAPACITY];
        private int _incomingHead;
        private int _outgoingHead;
        private int _incomingCount;
        private int _outgoingCount;

        private int _ppsInCount;
        private int _ppsOutCount;
        private float _incomingPps;
        private float _outgoingPps;
        private float _lastPpsTime;

        private bool _paused;

        private class InspectWindowState
        {
            public int Id;
            public PacketTrackData Data;
            public bool Open = true;
        }
        private List<InspectWindowState> _inspectWindows = new();
        private int _nextWindowId;

        // --- Bandwidth tracking ---
        private const int BW_HISTORY_SECONDS = 60;
        private class TypeBw
        {
            public string Name;
            public long TotalBytes;
            public int TotalCount;
            public float WindowBytes;
            public float WindowCount;
            public float[] History = new float[BW_HISTORY_SECONDS];
            public int HistoryIdx;
        }
        private Dictionary<string, TypeBw> _inBw = new();
        private Dictionary<string, TypeBw> _outBw = new();
        private bool _showBw = false;
        private int _bwView = 0; // 0 = combined, 1 = incoming, 2 = outgoing

        private int _pageSize = 100;
        private int _inPage;
        private int _outPage;

        public static PacketTracker Init()
        {
            using var _ = Profiler.Scope();
            if (_instance != null) return _instance;
            _instance = new PacketTracker();
            return _instance;
        }

        private static void BufferAdd(PacketTrackData[] buf, ref int head, ref int count, PacketTrackData data)
        {
            buf[head] = data;
            head = (head + 1) % BUFFER_CAPACITY;
            if (count < BUFFER_CAPACITY) count++;
        }

        private static PacketTrackData BufferGet(PacketTrackData[] buf, int head, int count, int index)
        {
            int idx = (head - 1 - index + BUFFER_CAPACITY) % BUFFER_CAPACITY;
            return buf[idx];
        }

        private static void RecordBw(Dictionary<string, TypeBw> dict, string name, int bytes)
        {
            if (!dict.TryGetValue(name, out var b))
                dict[name] = b = new TypeBw { Name = name };
            b.TotalBytes += bytes;
            b.TotalCount++;
            b.WindowBytes += bytes;
            b.WindowCount++;
        }

        private static void FinalizeBwSecond()
        {
            foreach (var b in _instance._inBw.Values) FinalizeOne(b);
            foreach (var b in _instance._outBw.Values) FinalizeOne(b);
        }

        private static void FinalizeOne(TypeBw b)
        {
            b.History[b.HistoryIdx] = b.WindowBytes;
            b.HistoryIdx = (b.HistoryIdx + 1) % BW_HISTORY_SECONDS;
            b.WindowBytes = 0;
            b.WindowCount = 0;
        }

        public static void TrackSent(PacketTrackData data)
        {
            if (_instance._paused) return;
            data.TrackId = _instance._nextTrackId++;
            data.Timestamp = Time.realtimeSinceStartup;
            BufferAdd(_instance._outgoingBuf, ref _instance._outgoingHead, ref _instance._outgoingCount, data);
            _instance._ppsOutCount++;
            RecordBw(_instance._outBw, data.packet.GetType().Name, data.size);
        }

        public static void TrackIncoming(PacketTrackData data)
        {
            if (_instance._paused) return;
            data.TrackId = _instance._nextTrackId++;
            data.Timestamp = Time.realtimeSinceStartup;
            BufferAdd(_instance._incomingBuf, ref _instance._incomingHead, ref _instance._incomingCount, data);
            _instance._ppsInCount++;
            RecordBw(_instance._inBw, data.packet.GetType().Name, data.size);
        }

        public void Clear()
        {
            using var _ = Profiler.Scope();
            _instance._incomingHead = 0;
            _instance._outgoingHead = 0;
            _instance._incomingCount = 0;
            _instance._outgoingCount = 0;
            _instance._ppsInCount = 0;
            _instance._ppsOutCount = 0;
            _instance._incomingPps = 0;
            _instance._outgoingPps = 0;
            _instance._inspectWindows.Clear();
            _instance._inBw.Clear();
            _instance._outBw.Clear();
            _paused = false;
        }

        private void CalculatePps()
        {
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _lastPpsTime;
            if (elapsed >= 1f)
            {
                _incomingPps = _ppsInCount / elapsed;
                _outgoingPps = _ppsOutCount / elapsed;
                _ppsInCount = 0;
                _ppsOutCount = 0;
                _lastPpsTime = now;
                FinalizeBwSecond();
            }
        }

        public void Toggle()
        {
            showWindow = !showWindow;
        }

        public void ShowWindow()
        {
            if (!showWindow) return;
            if (ImGui.Begin("Packet Tracker", ref showWindow))
            {
                DrawContent();
            }
            ImGui.End();
        }

        public void ShowInTab()
        {
            DrawContent();
        }

        private void DrawContent()
        {
            if (!MultiplayerSession.InSession)
            {
                if (_incomingCount > 0 || _outgoingCount > 0)
                    Clear();
                ImGui.TextDisabled("Not in a session!");
                return;
            }

            CalculatePps();

            // Top bar
            float lw = ImGui.GetContentRegionAvail().x;
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f),
                $"{_incomingPps:F1}/s");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f),
                $"{_outgoingPps:F1}/s");
            ImGui.SameLine();
            float totalBw = _inBw.Values.Sum(b => b.TotalBytes) + _outBw.Values.Sum(b => b.TotalBytes);
            float totalBwSec = _inBw.Values.Sum(b => AvgRecent(b)) + _outBw.Values.Sum(b => AvgRecent(b));
            ImGui.Text($"  {Utils.FormatBytes((long)totalBwSec)}/s");
            ImGui.SameLine();
            float textW = ImGui.CalcTextSize("Tracked: 50000/50000").x + 30;
            ImGui.Text($"  Tracked: {_incomingCount + _outgoingCount}");
            ImGui.SameLine(lw - textW - 100);
            ImGui.Checkbox("BW", ref _showBw);
            ImGui.SameLine();
            if (ImGui.Button(_paused ? "Resume" : "Pause"))
                _paused = !_paused;
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
                Clear();
            ImGui.Separator();

            if (ImGui.CollapsingHeader($"Incoming Packets##in_hdr", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.InputText("Filter##InFilter", ref incoming_filter, 64);
                ImGui.Separator();
                DrawTable("in_table", _incomingBuf, _incomingHead, _incomingCount, incoming_filter, ref _inPage);
            }

            ImGui.Separator();

            if (ImGui.CollapsingHeader($"Outgoing Packets##out_hdr", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.InputText("Filter##OutFilter", ref outgoing_filter, 64);
                ImGui.Separator();
                DrawTable("out_table", _outgoingBuf, _outgoingHead, _outgoingCount, outgoing_filter, ref _outPage);
            }

            if (_showBw)
            {
                ImGui.Separator();
                DrawBandwidth();
            }

            DrawInspectWindows();
        }

        private static float AvgRecent(TypeBw b)
        {
            float sum = 0;
            int n = 0;
            for (int i = 0; i < BW_HISTORY_SECONDS; i++)
            {
                float v = b.History[(b.HistoryIdx - 1 - i + BW_HISTORY_SECONDS) % BW_HISTORY_SECONDS];
                sum += v;
                if (v > 0) n++;
            }
            return n > 0 ? sum / n : 0;
        }

        public void DrawBandwidth()
        {
            string[] views = { "Combined", "Incoming", "Outgoing" };
            ImGui.Combo("View", ref _bwView, views, views.Length);
            ImGui.Separator();

            var combined = new Dictionary<string, TypeBw>();
            void Merge(Dictionary<string, TypeBw> src)
            {
                foreach (var kv in src)
                {
                    if (!combined.TryGetValue(kv.Key, out var c))
                        combined[kv.Key] = c = new TypeBw { Name = kv.Key };
                    c.TotalBytes += kv.Value.TotalBytes;
                    c.TotalCount += kv.Value.TotalCount;
                    for (int i = 0; i < BW_HISTORY_SECONDS; i++)
                        c.History[i] += kv.Value.History[i];
                }
            }

            List<TypeBw> items;
            if (_bwView == 0)
            {
                Merge(_inBw); Merge(_outBw);
                items = combined.Values.ToList();
            }
            else if (_bwView == 1)
                items = _inBw.Values.ToList();
            else
                items = _outBw.Values.ToList();

            if (items.Count == 0)
            {
                ImGui.TextDisabled("No bandwidth data yet.");
                return;
            }

            items.Sort((a, b) => -a.TotalBytes.CompareTo(b.TotalBytes));

            long grandTotal = items.Sum(i => i.TotalBytes);
            float curTotalBw = items.Sum(i => AvgRecent(i));

            ImGui.Text($"Total: {Utils.FormatBytes((long)curTotalBw)}/s  |  All time: {Utils.FormatBytes(grandTotal)}");
            ImGui.Spacing();

            float availW = ImGui.GetContentRegionAvail().x;
            if (ImGui.BeginTable("bw_table", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 300)))
            {
                ImGui.TableSetupColumn("Packet Type", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthFixed, 44);
                ImGui.TableSetupColumn("Bandwidth", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Pkts/s", ImGuiTableColumnFlags.WidthFixed, 55);
                ImGui.TableSetupColumn("Avg Size", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableHeadersRow();

                long maxBytes = items[0].TotalBytes;
                foreach (var item in items)
                {
                    float share = grandTotal > 0 ? (float)item.TotalBytes / grandTotal : 0;
                    float curBw = AvgRecent(item);
                    float curPps = item.TotalCount / Mathf.Max(1f, Time.realtimeSinceStartup);
                    int avgSize = item.TotalCount > 0 ? (int)(item.TotalBytes / item.TotalCount) : 0;

                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.Text(item.Name);

                    ImGui.TableNextColumn();
                    ImGui.Text($"{share * 100:F0}%");

                    ImGui.TableNextColumn();
                    ImGui.Text(Utils.FormatBytes((long)curBw) + "/s");

                    ImGui.TableNextColumn();
                    ImGui.Text($"{curPps:F1}");

                    ImGui.TableNextColumn();
                    ImGui.Text(Utils.FormatBytes(avgSize));

                    // Bar overlay on the row
                    if (maxBytes > 0)
                    {
                        var dl = ImGui.GetWindowDrawList();
                        var min = ImGui.GetItemRectMin();
                        var max = ImGui.GetItemRectMax();
                        float barW = (float)item.TotalBytes / maxBytes * (availW - 10);
                        uint col = ImGui.GetColorU32(new Vector4(0.3f, 0.6f, 1f, 0.25f));
                        dl.AddRectFilled(
                            new Vector2(min.x - 4, min.y),
                            new Vector2(min.x - 4 + barW, max.y),
                            col);
                    }
                }
                ImGui.EndTable();
            }

            // Mini bandwidth-over-time sparklines for top 5
            ImGui.Spacing();
            ImGui.Text("Bandwidth history (last 60s)");
            ImGui.Separator();

            var top5 = items.Take(5).ToList();
            float totalW = ImGui.GetContentRegionAvail().x;
            float sparkH = 40;

            foreach (var item in top5)
            {
                var col = ImGui.GetColorU32(new Vector4(0.4f, 0.7f, 1f, 0.8f));

                var pos = ImGui.GetCursorScreenPos();
                var size = new Vector2(totalW, sparkH);
                ImGui.Dummy(size);

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{item.Name}: {Utils.FormatBytes((long)AvgRecent(item))}/s");

                float peak = 1;
                for (int i = 0; i < BW_HISTORY_SECONDS; i++)
                {
                    float v = item.History[(item.HistoryIdx - 1 - i + BW_HISTORY_SECONDS) % BW_HISTORY_SECONDS];
                    if (v > peak) peak = v;
                }

                var dl = ImGui.GetWindowDrawList();
                float prevX = pos.x;
                float prevY = pos.y + sparkH;
                float sum = 0;
                for (int i = 0; i < BW_HISTORY_SECONDS; i++)
                {
                    float v = item.History[(item.HistoryIdx - 1 - i + BW_HISTORY_SECONDS) % BW_HISTORY_SECONDS];
                    float x = pos.x + totalW - (i / (float)(BW_HISTORY_SECONDS - 1)) * totalW;
                    float y = pos.y + sparkH - (v / peak) * sparkH;
                    dl.AddLine(new Vector2(prevX, prevY), new Vector2(x, y), col, 1.5f);
                    prevX = x;
                    prevY = y;
                    sum += v;
                }

                // Legend text beside sparkline
                float avg = sum / BW_HISTORY_SECONDS;
                var textPos = new Vector2(pos.x + 4, pos.y + 2);
                dl.AddText(textPos, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)),
                    $"{item.Name}");
                dl.AddText(new Vector2(pos.x + 4, pos.y + sparkH - 14),
                    ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1f)),
                    $"{Utils.FormatBytes((long)avg)}/s  peak: {Utils.FormatBytes((long)peak)}/s");
            }
        }

        private void OpenInspectWindow(PacketTrackData data)
        {
            _inspectWindows.RemoveAll(w => !w.Open);
            _inspectWindows.Add(new InspectWindowState
            {
                Id = _nextWindowId++,
                Data = data,
                Open = true
            });
        }

        private void DrawInspectWindows()
        {
            _inspectWindows.RemoveAll(w => !w.Open);
            foreach (var win in _inspectWindows)
            {
                if (!win.Open) continue;
                var data = win.Data;
                if (data.packet == null) { win.Open = false; continue; }

                var type = data.packet.GetType();
                string title = $"{type.Name}##inspect_{win.Id}";

                if (ImGui.Begin(title, ref win.Open))
                {
                    ImGui.TextDisabled($"Track ID: {data.TrackId}  |  Size: {Utils.FormatBytes(data.size)}");
                    ImGui.Separator();

                    var ifaces = type.GetInterfaces()
                        .Where(i => i != typeof(IPacket))
                        .Select(i => i.Name)
                        .ToList();
                    if (ifaces.Count > 0)
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
                            $"Implements: {string.Join(", ", ifaces)}");
                    ImGui.Spacing();

                    if (ImGui.BeginTable("pf", 4,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                    {
                        ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100);
                        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 50);
                        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableHeadersRow();

                        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                            .OrderBy(f => f.Name)
                            .ToList();

                        if (fields.Count == 0)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled("(no public instance fields)");
                        }

                        foreach (var field in fields)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text(field.Name);
                            ImGui.TableNextColumn();
                            ImGui.Text(GetFriendlyTypeName(field.FieldType));
                            ImGui.TableNextColumn();
                            ImGui.Text(GetFieldSizeHint(field.FieldType));
                            ImGui.TableNextColumn();
                            try
                            {
                                ImGui.Text(FormatFieldValue(field.GetValue(data.packet), field.FieldType));
                            }
                            catch
                            {
                                ImGui.TextDisabled("?");
                            }
                        }
                        ImGui.EndTable();
                    }
                }
                ImGui.End();
            }
        }

        private void DrawTable(string id, PacketTrackData[] buf, int head, int count, string filter, ref int page)
        {
            bool hasFilter = !string.IsNullOrEmpty(filter);

            if (ImGui.BeginTable(id, 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 350)))
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 32);
                ImGui.TableSetupColumn("Packet Type", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Age", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableHeadersRow();

                if (hasFilter)
                {
                    int shown = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var entry = BufferGet(buf, head, count, i);
                        string typeName = entry.packet.GetType().Name;
                        string idStr = entry.packet.GetType().GetHashCode().ToString();
                        if (typeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                            || idStr.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            DrawRow(entry, count - i);
                            shown++;
                        }
                    }
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled($"{shown} filtered entries");
                }
                else
                {
                    int totalPages = Math.Max(1, (count + _pageSize - 1) / _pageSize);
                    page = Math.Clamp(page, 0, totalPages - 1);

                    int startIdx = page * _pageSize;
                    int endIdx = Math.Min(startIdx + _pageSize, count);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        var entry = BufferGet(buf, head, count, i);
                        DrawRow(entry, count - i);
                    }
                }

                ImGui.EndTable();
            }

            // Page navigation
            if (!hasFilter && count > _pageSize)
            {
                int totalPages = Math.Max(1, (count + _pageSize - 1) / _pageSize);
                page = Math.Clamp(page, 0, totalPages - 1);

                if (ImGui.Button("< Prev") && page > 0)
                    page--;
                ImGui.SameLine();
                ImGui.Text($"  Page {page + 1}/{totalPages}  ({Math.Min(_pageSize, count - page * _pageSize)} entries)  ");
                ImGui.SameLine();
                if (ImGui.Button("Next >") && page < totalPages - 1)
                    page++;

                if (page != totalPages - 1)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Latest"))
                        page = 0;
                    if (ImGui.SmallButton("Oldest"))
                        page = totalPages - 1;
                }
            }
        }

        private void DrawRow(PacketTrackData entry, int index)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextDisabled($"{index + 1}");

            ImGui.TableNextColumn();
            ImGui.PushID(entry.TrackId);
            if (ImGui.Selectable(entry.packet.GetType().Name, false, ImGuiSelectableFlags.SpanAllColumns))
                OpenInspectWindow(entry);
            ImGui.PopID();

            ImGui.TableNextColumn();
            ImGui.Text(Utils.FormatBytes(entry.size));

            ImGui.TableNextColumn();
            float age = Time.realtimeSinceStartup - entry.Timestamp;
            if (age < 1f)
                ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"{age * 1000:F0}ms");
            else if (age < 60f)
                ImGui.Text($"{age:F1}s");
            else
                ImGui.Text($"{age / 60:F1}m");
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                var name = type.GetGenericTypeDefinition().Name;
                name = name.Substring(0, name.IndexOf('`'));
                var args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
                return $"{name}<{args}>";
            }
            if (type.IsEnum) return "enum";
            return type.Name;
        }

        private static string GetFieldSizeHint(Type type)
        {
            if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)) return "1";
            if (type == typeof(short) || type == typeof(ushort))                   return "2";
            if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return "4";
            if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return "8";
            if (type == typeof(Vector2))  return "8";
            if (type == typeof(Vector3))  return "12";
            if (type == typeof(Vector4) || type == typeof(Quaternion)) return "16";
            if (type == typeof(Color))    return "16";
            if (type == typeof(string))   return "~";
            if (type.IsEnum) return GetFieldSizeHint(Enum.GetUnderlyingType(type));
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return "~";
            if (type.IsArray)  return "~";
            if (!type.IsValueType) return "~";
            return "?";
        }

        private static string FormatFieldValue(object val, Type type)
        {
            if (val == null) return "null";
            if (type == typeof(Vector3))
            {
                var v = (Vector3)val;
                return $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
            }
            if (type == typeof(Vector2))
            {
                var v = (Vector2)val;
                return $"({v.x:F3}, {v.y:F3})";
            }
            if (type == typeof(Color))
            {
                var c = (Color)val;
                return $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
            }
            if (type == typeof(string))
            {
                var s = (string)val;
                if (string.IsNullOrEmpty(s)) return "\"\"";
                if (s.Length > 60) return $"\"{s.Substring(0, 60)}...\"";
                return $"\"{s}\"";
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (System.Collections.IList)val;
                return $"[Count: {list?.Count ?? 0}]";
            }
            if (type.IsArray)
            {
                var arr = (Array)val;
                return $"[Length: {arr?.Length ?? 0}]";
            }
            if (type.IsEnum) return $"{val}";
            return val.ToString() ?? "";
        }
    }
}
