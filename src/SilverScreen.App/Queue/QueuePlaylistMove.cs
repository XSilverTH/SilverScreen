using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Queue;

/// <summary>
/// Translates a queue reorder into the single playlist move represented by a
/// drag operation. The item identity, rather than the first positional
/// mismatch, identifies the item that was moved.
/// </summary>
internal static class QueuePlaylistMove
{
    public static bool TryTranslate(
        IReadOnlyList<VideoSummary> current,
        IReadOnlyList<VideoSummary> next,
        out int fromIndex,
        out int toIndex)
    {
        fromIndex = -1;
        toIndex = -1;

        if (current.Count != next.Count || current.Count < 2)
            return false;

        for (var candidateIndex = 0; candidateIndex < current.Count; candidateIndex++)
        {
            var candidateId = current[candidateIndex].Id;
            var destinationIndex = IndexOf(next, candidateId);
            if (destinationIndex < 0 || destinationIndex == candidateIndex)
                continue;

            // Remove the candidate from the old order, then insert it where it
            // appears in the new order. Only that identity can explain a
            // single drag reorder; this also handles moves in either direction
            // and swaps.
            var expected = new List<string>(current.Count - 1);
            for (var index = 0; index < current.Count; index++)
                if (index != candidateIndex)
                    expected.Add(current[index].Id);
            expected.Insert(destinationIndex, candidateId);

            var matches = true;
            for (var index = 0; index < next.Count; index++)
            {
                if (!string.Equals(expected[index], next[index].Id, StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                fromIndex = candidateIndex;
                toIndex = destinationIndex;
                return true;
            }
        }

        return false;
    }

    private static int IndexOf(IReadOnlyList<VideoSummary> videos, string id)
    {
        for (var index = 0; index < videos.Count; index++)
            if (string.Equals(videos[index].Id, id, StringComparison.Ordinal))
                return index;

        return -1;
    }
}
