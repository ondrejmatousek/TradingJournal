namespace TradingJournal.Services
{
    public class AppClock
    {
        private readonly TimeZoneInfo _timeZone;

        public AppClock()
        {
            _timeZone = ResolvePragueTimeZone();
        }

        public DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);

        public DateTimeOffset ToAppTime(DateTimeOffset value)
        {
            return TimeZoneInfo.ConvertTime(value, _timeZone);
        }

        public string FormatShort(DateTimeOffset value)
        {
            return ToAppTime(value).ToString("dd.MM. HH:mm");
        }

        public string FormatTime(DateTimeOffset value)
        {
            return ToAppTime(value).ToString("HH:mm");
        }

        private static TimeZoneInfo ResolvePragueTimeZone()
        {
            foreach (var id in new[] { "Central Europe Standard Time", "Europe/Prague" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.Local;
        }
    }
}
