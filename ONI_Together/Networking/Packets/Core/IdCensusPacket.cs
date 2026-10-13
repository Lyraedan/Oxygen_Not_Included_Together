using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.Core
{
    /// <summary>
    /// A slice of what the host holds, so a client can notice what it is missing without
    /// anyone having to know in advance what kind of object went astray.
    ///
    /// The mod has good instruments for one peer at a time - the network overlay, the
    /// packet tracker, the sync stats, the per-NetId activity tracker. None of them
    /// compares the two peers, so "the client does not have that object" is only ever
    /// found by a player noticing something missing and somebody then reading two logs
    /// side by side. Every defect of that shape I have chased was found that way, one
    /// object type at a time.
    ///
    /// This asks the general question continuously and cheaply. The host walks its own
    /// NetworkIdentity objects, forty ids at a time, four times a second, and the client
    /// looks each one up. That is 640 bytes a second of payload, and a ten thousand object
    /// colony is covered end to end in about a minute.
    ///
    /// Ids only. Prefab names would make the report readable on the spot and would also
    /// push the packet past the 1000-byte payload limit into the chunking path. The client
    /// cannot name an object it does not have, but the host's log can - grep the id.
    ///
    /// It reports and does not repair. Creating a missing object from an announcement is a
    /// separate question with its own failure modes; the point here is that the absence
    /// stops being invisible.
    /// </summary>
    public class IdCensusPacket : IPacket
    {
        /// <summary>
        /// Which pass over the host's objects this batch belongs to.
        ///
        /// The client needs pass boundaries to tell a real absence from an object still in
        /// flight. Something announced a moment ago is legitimately missing right now and
        /// will be there next pass; something missing on two consecutive passes is not in
        /// flight, it is gone. Without this the report is mostly noise, and noise in a
        /// warning is how a real signal gets ignored.
        /// </summary>
        public int Cycle;

        public int[] NetIds;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Cycle);
            writer.Write(NetIds?.Length ?? 0);
            if (NetIds != null)
                foreach (int id in NetIds)
                    writer.Write(id);
        }

        public void Deserialize(BinaryReader reader)
        {
            Cycle = reader.ReadInt32();
            int count = reader.ReadInt32();

            // A length read off the wire is a length somebody else chose.
            if (count < 0 || count > 4096) { NetIds = new int[0]; return; }

            NetIds = new int[count];
            for (int i = 0; i < count; i++)
                NetIds[i] = reader.ReadInt32();
        }

        public void OnDispatched()
        {
            if (MultiplayerSession.IsHost) return;
            IdCensus.Receive(Cycle, NetIds);
        }
    }
}
