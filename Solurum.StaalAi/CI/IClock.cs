namespace Solurum.StaalAi.CI
{
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }
}