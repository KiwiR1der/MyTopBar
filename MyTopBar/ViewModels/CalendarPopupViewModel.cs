using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTopBar.Models;
using System.Collections.ObjectModel;

namespace MyTopBar.ViewModels
{
    public partial class CalendarPopupViewModel : ObservableObject
    {
        [ObservableProperty]
        private DateTime _currentMonth = DateTime.Today;

        public ObservableCollection<CalendarDayModel> Days { get; } = [];

        public CalendarPopupViewModel()
        {
            GenerateCalendar();
        }

        [RelayCommand]
        private void PreviousMonth()
        {
            CurrentMonth = CurrentMonth.AddMonths(-1);
            GenerateCalendar();
        }

        [RelayCommand]
        private void NextMonth()
        {
            CurrentMonth = CurrentMonth.AddMonths(1);
            GenerateCalendar();
        }

        public void ResetToCurrentMonth()
        {
            CurrentMonth = DateTime.Today;
            GenerateCalendar();
        }

        private void GenerateCalendar()
        {
            Days.Clear();

            var firstDayOfMonth = new DateTime(CurrentMonth.Year, CurrentMonth.Month, 1);
            int dayOfWeek = (int)firstDayOfMonth.DayOfWeek;
            int offset = (dayOfWeek == 0 ? 7 : dayOfWeek) - 1;
            DateTime startDate = firstDayOfMonth.AddDays(-offset);

            for (int i = 0; i < 42; i++)
            {
                DateTime date = startDate.AddDays(i);
                Days.Add(new CalendarDayModel
                {
                    Date = date,
                    Day = date.Day,
                    IsCurrentMonth = date.Month == CurrentMonth.Month,
                    IsToday = date.Date == DateTime.Today
                });
            }
        }
    }
}
