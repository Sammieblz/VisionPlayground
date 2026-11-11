namespace CoDriver
{
    public enum TrafficDensity
    {
        Normal,
        RushHour,
    }

    public static class TrafficPopulation
    {
        // Bus, emergency vehicle and parking car retain indices 12, 13 and 14.
        // Ordinary vehicles beyond index 7 follow local seeded circuits, so a
        // higher setting adds encounters near the starting district.
        public static int OrdinaryCount(TrafficDensity density) =>
            density == TrafficDensity.RushHour ? 32 : 20;
    }
}
