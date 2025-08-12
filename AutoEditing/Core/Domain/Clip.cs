namespace Core
{

    public class Clip
    {
        public string Path { get; }

        public string Name { get; }
        public string Game {  get; }
        public string Map { get; }
        public string Gun { get; }
        public string Player { get; }
        public string PostFix { get; } // 001, 002, etc.

        public int Shots { get; }
        public int Length { get; }

        public int Bitrate { get; }
        public Resolution Resolution { get; }
        public double FramesPerSecond { get; }

        public Clip(string name)
        {
            // TODO: based on name compute info
        }
    }
}
