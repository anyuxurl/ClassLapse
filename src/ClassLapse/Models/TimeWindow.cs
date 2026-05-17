namespace ClassLapse.Models;

public sealed class TimeWindow
{
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }

    public TimeWindow() { }

    public TimeWindow(TimeOnly start, TimeOnly end)
    {
        Start = start;
        End = end;
    }

    public bool Contains(TimeOnly t) => t >= Start && t < End;
}
