using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration
{
    public sealed class MsgUpdateAdminStatus : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.Command;

        public AdminData? Admin;
        public string[] AvailableCommands = Array.Empty<string>();

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var count = buffer.ReadVariableInt32();

            AvailableCommands = new string[count];

            for (var i = 0; i < count; i++)
            {
                AvailableCommands[i] = buffer.ReadString();
            }

            if (buffer.ReadBoolean())
            {
                var active = buffer.ReadBoolean();
                buffer.ReadPadBits();
                var flags = (AdminFlags) buffer.ReadUInt32();
                var title = buffer.ReadString();

                // _Duty: узлы дерева прав
                var nodeCount = buffer.ReadVariableInt32();
                var nodes = new HashSet<string>(nodeCount);
                for (var i = 0; i < nodeCount; i++)
                {
                    nodes.Add(buffer.ReadString());
                }

                Admin = new AdminData
                {
                    Active = active,
                    Title = title,
                    Flags = flags,
                    Nodes = nodes,
                };
            }

        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.WriteVariableInt32(AvailableCommands.Length);

            foreach (var cmd in AvailableCommands)
            {
                buffer.Write(cmd);
            }

            buffer.Write(Admin != null);

            if (Admin == null) return;

            buffer.Write(Admin.Active);
            buffer.WritePadBits();
            buffer.Write((uint) Admin.Flags);
            buffer.Write(Admin.Title);

            // _Duty: узлы дерева прав
            buffer.WriteVariableInt32(Admin.Nodes.Count);
            foreach (var node in Admin.Nodes)
            {
                buffer.Write(node);
            }
        }

        public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}
