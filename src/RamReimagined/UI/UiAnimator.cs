using System.Diagnostics;

namespace RamReimagined.UI;

/// <summary>
/// Shared UI animation clock. One timer drives every morph so hover, selection
/// and page transitions stay in step instead of spawning a timer per control.
/// </summary>
internal static class UiAnimator
{
    private sealed class Entry
    {
        public object Owner = null!;
        public string Key = "";
        public float From;
        public float To;
        public float Current;
        public int Duration;
        public long Started;
        public Action<float> Apply = null!;
        public Func<float, float> Ease = Theme.EaseOutCubic;
        public Action? Done;
    }

    private static readonly System.Windows.Forms.Timer AnimationClock = new() { Interval = 15 };
    private static readonly List<Entry> ActiveEntries = new();
    private static readonly Stopwatch StopwatchClock = Stopwatch.StartNew();

    static UiAnimator()
    {
        AnimationClock.Tick += (_, _) => Tick();
    }

    public static void Animate(object Owner, string Key, float From, float To, int DurationMilliseconds,
        Action<float> Apply, Func<float, float>? Ease = null, Action? Done = null)
    {
        var Index = ActiveEntries.FindIndex(Candidate =>
            ReferenceEquals(Candidate.Owner, Owner) && Candidate.Key == Key);
        if (Index >= 0)
        {
            // Continue from wherever the running morph is right now, so rapid
            // hover/selection changes never snap back to an old value.
            var Running = ActiveEntries[Index];
            From = Running.Current;
            ActiveEntries.RemoveAt(Index);
        }

        if (Math.Abs(To - From) < 0.001f)
        {
            Apply(To);
            Done?.Invoke();
            return;
        }

        ActiveEntries.Add(new Entry
        {
            Owner = Owner,
            Key = Key,
            From = From,
            To = To,
            Current = From,
            Duration = Math.Max(1, DurationMilliseconds),
            Started = StopwatchClock.ElapsedMilliseconds,
            Apply = Apply,
            Ease = Ease ?? Theme.EaseOutCubic,
            Done = Done
        });

        if (!AnimationClock.Enabled)
        {
            AnimationClock.Start();
        }
    }

    public static void Cancel(object Owner, string Key)
    {
        ActiveEntries.RemoveAll(Candidate => ReferenceEquals(Candidate.Owner, Owner) && Candidate.Key == Key);
    }

    private static void Tick()
    {
        if (ActiveEntries.Count == 0)
        {
            AnimationClock.Stop();
            return;
        }

        var Now = StopwatchClock.ElapsedMilliseconds;
        for (var Index = ActiveEntries.Count - 1; Index >= 0; Index--)
        {
            var Entry = ActiveEntries[Index];
            if (Entry.Owner is Control OwnerControl && OwnerControl.IsDisposed)
            {
                ActiveEntries.RemoveAt(Index);
                continue;
            }

            var Raw = Math.Clamp((Now - Entry.Started) / (float)Entry.Duration, 0f, 1f);
            var Eased = Entry.Ease(Raw);
            Entry.Current = Entry.From + (Entry.To - Entry.From) * Eased;
            Entry.Apply(Entry.Current);

            if (Raw >= 1f)
            {
                ActiveEntries.RemoveAt(Index);
                Entry.Done?.Invoke();
            }
        }
    }
}
