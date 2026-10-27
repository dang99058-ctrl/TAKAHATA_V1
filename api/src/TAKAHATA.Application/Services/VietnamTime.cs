namespace TAKAHATA.Application.Services
{
    public static class VietnamTime
    {
        public static TimeZoneInfo TimeZone { get; } =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows()
                    ? "SE Asia Standard Time"
                    : "Asia/Ho_Chi_Minh");

        public static DateTime Now =>
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZone).DateTime;

        public static DateOnly Today => DateOnly.FromDateTime(Now);
    }
}
