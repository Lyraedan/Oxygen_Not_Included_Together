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
        public static PacketTracker Instance => _instance;
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
            public HashSet<string> ExpandedFields = new();
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

        private float _bwSnapshotTimer;
        private const float BW_SNAPSHOT_INTERVAL = 10f;
        private Dictionary<string, float[]> _bwSnapshots = new();

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

        private static void ResetHistory(TypeBw b)
        {
            Array.Clear(b.History, 0, BW_HISTORY_SECONDS);
            b.HistoryIdx = 0;
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

        public void CalculatePps()
        {
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _lastPpsTime;
            if (elapsed >= 1f)
            {
                _incomingPps = Mathf.Round(_ppsInCount / elapsed);
                _outgoingPps = Mathf.Round(_ppsOutCount / elapsed);
                _ppsInCount = 0;
                _ppsOutCount = 0;
                _lastPpsTime = now;
                FinalizeBwSecond();
            }
        }

        public static float IncomingPps => _instance._incomingPps;
        public static float OutgoingPps => _instance._outgoingPps;
        public static int IncomingCount => _instance._incomingCount;
        public static int OutgoingCount => _instance._outgoingCount;
        public static float IncomingBandwidth => _instance._inBw.Values.Sum(b => AvgRecent(b));
        public static float OutgoingBandwidth => _instance._outBw.Values.Sum(b => AvgRecent(b));

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
            float totalRecentBw = items.Sum(i => AvgRecent(i));
            float totalLifeBw = items.Sum(i => i.TotalBytes) / Mathf.Max(0.1f, Time.realtimeSinceStartup);

            ImGui.Text($"Total: {Utils.FormatBytes((long)totalRecentBw)}/s  |  All time: {Utils.FormatBytes(grandTotal)}");
            ImGui.Spacing();

            float availW = ImGui.GetContentRegionAvail().x;
            if (ImGui.BeginTable("bw_table", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 300)))
            {
                ImGui.TableSetupColumn("Packet Type", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthFixed, 55);
                ImGui.TableSetupColumn("Bandwidth", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Pkts/s", ImGuiTableColumnFlags.WidthFixed, 55);
                ImGui.TableSetupColumn("Avg Size", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableHeadersRow();

                long maxBytes = items[0].TotalBytes;
                float elapsed = Mathf.Max(0.1f, Time.realtimeSinceStartup);
                foreach (var item in items)
                {
                    float curBw = AvgRecent(item);
                    if (curBw <= 0f)
                        curBw = item.TotalBytes / elapsed;

                    float share = totalRecentBw > 0
                        ? AvgRecent(item) / totalRecentBw
                        : (grandTotal > 0 ? (float)item.TotalBytes / grandTotal : 0);

                    float curPps = item.TotalCount / elapsed;
                    int avgSize = item.TotalCount > 0 ? (int)(item.TotalBytes / item.TotalCount) : 0;

                    ImGui.TableNextRow();
                    var rowMin = ImGui.GetCursorScreenPos();

                    ImGui.TableNextColumn();
                    ImGui.Text(item.Name);

                    ImGui.TableNextColumn();
                    float sharePercent = share * 100;
                    ImGui.Text($"{sharePercent:F1}%"); // Why tf are you displaying gibberish

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
                        var rowMax = ImGui.GetItemRectMax();
                        float barW = (float)item.TotalBytes / maxBytes * (availW - 10);
                        uint col = ImGui.GetColorU32(new Vector4(0.3f, 0.6f, 1f, 0.25f));
                        dl.AddRectFilled(
                            new Vector2(rowMin.x, rowMin.y),
                            new Vector2(rowMin.x + barW, rowMax.y),
                            col);
                    }
                }
                ImGui.EndTable();
            }

            // Capture display snapshots periodically (don't touch live History)
            _bwSnapshotTimer += ImGui.GetIO().DeltaTime;
            if (_bwSnapshotTimer >= BW_SNAPSHOT_INTERVAL)
            {
                _bwSnapshotTimer = 0f;
                _bwSnapshots.Clear();
                float[] snap = new float[BW_HISTORY_SECONDS];
                foreach (var item in items)
                {
                    for (int i = 0; i < BW_HISTORY_SECONDS; i++)
                        snap[i] = item.History[(item.HistoryIdx + i) % BW_HISTORY_SECONDS];
                    _bwSnapshots[item.Name] = (float[])snap.Clone();
                }
            }

            // Mini bandwidth-over-time sparklines for top 5
            ImGui.Spacing();
            ImGui.Text("Bandwidth history (last 60s)");
            ImGui.Separator();

            // This shit just kinda broke atm
            var top5 = items.Take(5).ToList();
            float totalW = ImGui.GetContentRegionAvail().x;
            float sparkH = 40;

            float[] chrono = new float[BW_HISTORY_SECONDS];
            foreach (var item in top5)
            {
                float[] src;
                if (!_bwSnapshots.TryGetValue(item.Name, out src))
                {
                    for (int i = 0; i < BW_HISTORY_SECONDS; i++)
                        chrono[i] = item.History[(item.HistoryIdx + i) % BW_HISTORY_SECONDS];
                    src = chrono;
                }

                float peak = 0f, sum = 0;
                for (int i = 0; i < BW_HISTORY_SECONDS; i++)
                {
                    if (src[i] > peak) peak = src[i];
                    sum += src[i];
                }
                float avg = sum / BW_HISTORY_SECONDS;
                float scaleMax = Mathf.Max(peak * 1.1f, 1f);

                string overlay = $"{Utils.FormatBytes((long)avg)}/s  pk: {Utils.FormatBytes((long)peak)}/s";
                ImGui.PlotLines($"##bw_{item.Name}", ref src[0], BW_HISTORY_SECONDS, 0,
                    overlay, 0f, scaleMax, new Vector2(totalW, sparkH));
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
                    $"{item.Name}  -  {overlay}");
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
                    string header = $"Track ID: {data.TrackId}  |  Size: {Utils.FormatBytes(data.size)}";
                    var netIdField = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(f => f.Name.Equals("NetID", StringComparison.OrdinalIgnoreCase));
                    if (netIdField != null)
                    {
                        try
                        {
                            int netId = (int)netIdField.GetValue(data.packet);
                            if (NetworkIdentityRegistry.TryGet(netId, out var identity))
                                header += $"  |  Network Object: {string.Format(global::STRINGS.UI.StripLinkFormatting(identity.gameObject.name))}";
                            else
                                header += "  |  Network Object: ~";
                        }
                        catch { header += "  |  Network Object: ~"; }
                    }
                    ImGui.TextDisabled(header);
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
                        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 160);
                        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 50);
                        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableHeadersRow();

                        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
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
                            if (field.IsPublic)
                                ImGui.Text(field.Name);
                            else
                                ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), field.Name);
                            ImGui.TableNextColumn();
                            try
                            {
                                object val = field.GetValue(data.packet);
                                string typeName = GetFriendlyTypeName(field.FieldType, val);
                                ImGui.Text(typeName);
                                if (ImGui.IsItemHovered() && typeName.Length > 15)
                                    ImGui.SetTooltip(typeName);
                                ImGui.TableNextColumn();
                                ImGui.Text(GetFieldSizeHint(val, field.FieldType));
                                ImGui.TableNextColumn();
                                bool isList = field.FieldType.IsGenericType
                                    && field.FieldType.GetGenericTypeDefinition() == typeof(List<>);
                                bool isArr = field.FieldType.IsArray;
                                bool isDict = field.FieldType.IsGenericType
                                    && field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>);

                                if ((isList || isArr) && val != null)
                                {
                                    var list = (System.Collections.IList)val;
                                    int count = list.Count;
                                    string label = isList ? $"[Count: {count}]" : $"[Length: {count}]";
                                    bool expanded = win.ExpandedFields.Contains(field.Name);
                                    if (ImGui.Selectable(label, expanded))
                                    {
                                        if (expanded) win.ExpandedFields.Remove(field.Name);
                                        else win.ExpandedFields.Add(field.Name);
                                    }

                                    if (expanded)
                                    {
                                        int maxShow = Math.Min(count, 100);
                                        Type elemType = isList
                                            ? field.FieldType.GetGenericArguments()[0]
                                            : field.FieldType.GetElementType();
                                        for (int i = 0; i < maxShow; i++)
                                        {
                                            ImGui.TableNextRow();
                                            ImGui.TableNextColumn();
                                            ImGui.Text($"  [{i}]");
                                            ImGui.TableNextColumn();
                                            try
                                            {
                                                object elemVal = list[i];
                                                string elemTypeName = GetFriendlyTypeName(elemType, elemVal);
                                                ImGui.Text(elemTypeName);
                                                if (ImGui.IsItemHovered() && elemTypeName.Length > 15)
                                                    ImGui.SetTooltip(elemTypeName);
                                                ImGui.TableNextColumn();
                                                ImGui.Text(GetFieldSizeHint(elemVal, elemType));
                                                ImGui.TableNextColumn();
                                                ImGui.Text(FormatFieldValue(elemVal, elemType));
                                            }
                                            catch
                                            {
                                                ImGui.TextDisabled("?");
                                            }
                                        }
                                        if (count > maxShow)
                                        {
                                            ImGui.TableNextRow();
                                            ImGui.TableNextColumn();
                                            ImGui.Text($"  ... and {count - maxShow} more");
                                        }
                                    }
                                }
                                else if (isDict && val != null)
                                {
                                    var dict = (System.Collections.IDictionary)val;
                                    int count = dict.Count;
                                    bool expanded = win.ExpandedFields.Contains(field.Name);
                                    if (ImGui.Selectable($"[Count: {count}]", expanded))
                                    {
                                        if (expanded) win.ExpandedFields.Remove(field.Name);
                                        else win.ExpandedFields.Add(field.Name);
                                    }

                                    if (expanded)
                                    {
                                        int maxShow = Math.Min(count, 100);
                                        Type keyType = field.FieldType.GetGenericArguments()[0];
                                        Type valType = field.FieldType.GetGenericArguments()[1];
                                        int shown = 0;
                                        foreach (System.Collections.DictionaryEntry entry in dict)
                                        {
                                            if (shown >= maxShow) break;
                                            ImGui.TableNextRow();
                                            ImGui.TableNextColumn();
                                            ImGui.Text($"  [{FormatFieldValue(entry.Key, keyType)}]");
                                            ImGui.TableNextColumn();
                                            try
                                            {
                                                string valTypeName = GetFriendlyTypeName(valType, entry.Value);
                                                ImGui.Text(valTypeName);
                                                if (ImGui.IsItemHovered() && valTypeName.Length > 15)
                                                    ImGui.SetTooltip(valTypeName);
                                                ImGui.TableNextColumn();
                                                ImGui.Text(GetFieldSizeHint(entry.Value, valType));
                                                ImGui.TableNextColumn();
                                                ImGui.Text(FormatFieldValue(entry.Value, valType));
                                            }
                                            catch
                                            {
                                                ImGui.TextDisabled("?");
                                            }
                                            shown++;
                                        }
                                        if (count > maxShow)
                                        {
                                            ImGui.TableNextRow();
                                            ImGui.TableNextColumn();
                                            ImGui.Text($"  ... and {count - maxShow} more");
                                        }
                                    }
                                }
                                else
                                {
                                    ImGui.Text(FormatFieldValue(val, field.FieldType));
                                }
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
            List<int> matchedIndices = null;
            int visibleCount;

            if (hasFilter)
            {
                matchedIndices = new List<int>(Math.Min(count, 256));
                for (int i = 0; i < count; i++)
                {
                    var entry = BufferGet(buf, head, count, i);
                    string typeName = entry.packet.GetType().Name;
                    string idStr = entry.packet.GetType().GetHashCode().ToString();
                    if (typeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                        || idStr.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matchedIndices.Add(i);
                    }
                }
                visibleCount = matchedIndices.Count;
            }
            else
            {
                visibleCount = count;
            }

            int totalPages = Math.Max(1, (visibleCount + _pageSize - 1) / _pageSize);
            page = Math.Clamp(page, 0, totalPages - 1);

            if (ImGui.BeginTable(id, 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 350)))
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 32);
                ImGui.TableSetupColumn("Packet Type", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Age", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableHeadersRow();

                int startIdx = page * _pageSize;
                int endIdx = Math.Min(startIdx + _pageSize, visibleCount);

                for (int idx = startIdx; idx < endIdx; idx++)
                {
                    int i = hasFilter ? matchedIndices[idx] : idx;
                    var entry = BufferGet(buf, head, count, i);
                    DrawRow(entry, count - i);
                }

                ImGui.EndTable();
            }

            // Page navigation
            if (visibleCount > _pageSize)
            {
                if (ImGui.Button("< Prev") && page > 0)
                    page--;
                ImGui.SameLine();
                ImGui.Text($"  Page {page + 1}/{totalPages}  ({Math.Min(_pageSize, visibleCount - page * _pageSize)} entries)  ");
                ImGui.SameLine();
                if (ImGui.Button("Next >") && page < totalPages - 1)
                    page++;

                ImGui.SameLine();
                if (ImGui.SmallButton("Latest"))
                    page = 0;
                ImGui.SameLine();
                if (ImGui.SmallButton("Oldest"))
                    page = totalPages - 1;
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
                var args = string.Join(", ", type.GetGenericArguments().Select(t => GetFriendlyTypeName(t)));
                return $"{name}<{args}>";
            }
            if (type.IsEnum) return "enum";
            return type.Name;
        }

        private static string GetFriendlyTypeName(Type type, object val)
        {
            if (type == typeof(Variant))
                return $"Variant ({((Variant)val).Type})";
            return GetFriendlyTypeName(type);
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
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(Dictionary<,>))) return "~";
            if (type.IsArray)  return "~";
            if (type == typeof(Variant)) return "~";
            if (!type.IsValueType) return "~";
            return "?";
        }

        private static string GetFieldSizeHint(object val, Type type)
        {
            if (type == typeof(Variant))
            {
                var v = (Variant)val;
                return v.Type switch
                {
                    Variant.TypeCode.Float   => "4",
                    Variant.TypeCode.Int     => "4",
                    Variant.TypeCode.Byte    => "1",
                    Variant.TypeCode.String  => "~",
                    Variant.TypeCode.Boolean => "1",
                    Variant.TypeCode.Vector3 => "12",
                    Variant.TypeCode.Vector2 => "8",
                    Variant.TypeCode.ByteArray => "~",
                    _ => "?"
                };
            }
            return GetFieldSizeHint(type);
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
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var dict = (System.Collections.IDictionary)val;
                return $"[Count: {dict?.Count ?? 0}]";
            }
            if (type.IsArray)
            {
                var arr = (Array)val;
                return $"[Length: {arr?.Length ?? 0}]";
            }
            if (type == typeof(Variant))
            {
                var v = (Variant)val;
                return v.Type switch
                {
                    Variant.TypeCode.Float   => v.Float.ToString("F4"),
                    Variant.TypeCode.Int     => v.Int.ToString(),
                    Variant.TypeCode.Byte    => v.Byte.ToString(),
                    Variant.TypeCode.String  => $"\"{v.String}\"",
                    Variant.TypeCode.Boolean => v.Boolean.ToString(),
                    Variant.TypeCode.Vector3 => $"({v.Vector3.x:F3}, {v.Vector3.y:F3}, {v.Vector3.z:F3})",
                    Variant.TypeCode.Vector2 => $"({v.Vector2.x:F3}, {v.Vector2.y:F3})",
                    Variant.TypeCode.ByteArray => $"byte[{v.ByteArray?.Length ?? 0}]",
                    _ => $"? ({(byte)v.Type})"
                };
            }
            if (type.IsEnum) return $"{val}";
            return val.ToString() ?? "";
        }
    }
}
