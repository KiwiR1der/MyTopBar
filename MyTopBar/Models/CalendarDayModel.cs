namespace MyTopBar.Models
{
    public sealed class CalendarDayModel
    {
        public DateTime Date { get; init; }
        public int Day { get; init; }
        public bool IsCurrentMonth { get; init; }
        public bool IsToday { get; init; }
    }
}
