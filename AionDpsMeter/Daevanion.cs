using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record DaevanionBoard(int Board, int[] Nodes);
public sealed record DaevanionSnapshot(long Timestamp, DaevanionBoard[] Boards);
internal static class Daevanion
{
    internal static void Validate(DaevanionSnapshot state)
    {
        if (state.Timestamp < 0 || state.Boards == null || state.Boards.Length > 64 ||
            state.Boards.Any(b => b == null || b.Board <= 0 || b.Nodes == null || b.Nodes.Length > 1024 || b.Nodes.Any(n => n <= 0) || b.Nodes.Distinct().Count() != b.Nodes.Length) ||
            state.Boards.Select(b => b.Board).Distinct().Count() != state.Boards.Length)
            throw new InvalidDataException("Invalid Daevanion board snapshot.");
    }
    internal static DaevanionBoard[] Read(ref WireReader reader)
    {
        int count = reader.Var(); if (count > 64) throw new InvalidDataException("Too many Daevanion boards.");
        var boards = new DaevanionBoard[count];
        for (int i = 0; i < count; i++) {
            int board = checked((int)reader.U32()); int size = reader.Var();
            if (size > 1024) throw new InvalidDataException("Too many Daevanion nodes.");
            var nodes = new int[size]; for (int j = 0; j < size; j++) nodes[j] = checked((int)reader.U32());
            boards[i] = new(board, nodes);
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown Daevanion trailer.");
        Validate(new(0, boards)); return boards;
    }
    internal static DaevanionSnapshot? Apply(DaevanionSnapshot? old, NetworkEvent message)
    {
        if (message.Timestamp < 0 || old != null && message.Timestamp < old.Timestamp) return old;
        var next = new DaevanionSnapshot(message.Timestamp, message.Fields.GetProperty("Boards").Deserialize<DaevanionBoard[]>()!);
        Validate(next);
        return next with { Boards = next.Boards.Select(b => b with { Nodes = b.Nodes.ToArray() }).ToArray() };
    }
}
