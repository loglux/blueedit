/// <summary>A place in a document: Path is null for one not saved yet.</summary>
readonly record struct Place(string? Path, int Row, int Col);

/// <summary>
/// Where the jumps left from, for back and forward (docs/tech/editing.md). Only jumps are recorded, not arrows or typing.
/// A run of one kind of jump (F5 again and again) counts as one, so back returns to where the run began.
/// </summary>
sealed class JumpHistory
{
    const int Limit = 100;
    readonly List<Place> _back = new(), _forward = new();
    string? _kind;
    Place _landed;

    public void Record(Place from, Place to, string? kind = null)
    {
        if (from == to) return;
        bool sameRun = kind != null && kind == _kind && from == _landed;
        _kind = kind; _landed = to;
        if (sameRun) return;
        _back.Add(from);
        if (_back.Count > Limit) _back.RemoveAt(0);
        _forward.Clear();
    }

    public Place? Back(Place here) => Step(_back, _forward, here);

    public Place? Forward(Place here) => Step(_forward, _back, here);

    // the latest place that is not this one; this one goes on the other list, once, however many are stepped over
    Place? Step(List<Place> from, List<Place> to, Place here)
    {
        _kind = null;
        while (from.Count > 0)
        {
            var place = from[^1];
            from.RemoveAt(from.Count - 1);
            if (place == here) continue;
            if (to.Count == 0 || to[^1] != here) to.Add(here);
            return place;
        }
        return null;
    }
}
