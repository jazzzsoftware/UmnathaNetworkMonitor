using System;

namespace NetworkMonitor.Core.Update
{
    public sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private SemanticVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int Major
        {
            get;
        }

        public int Minor
        {
            get;
        }

        public int Patch
        {
            get;
        }

        public static bool TryParse(string text, out SemanticVersion version)
        {
            version = new SemanticVersion(0, 0, 0);
            bool parsed = false;

            if (!string.IsNullOrWhiteSpace(text))
            {
                string candidate = text.Trim();

                if (candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidate.Substring(1);
                }

                int dashIndex = candidate.IndexOf('-');

                if (dashIndex >= 0)
                {
                    candidate = candidate.Substring(0, dashIndex);
                }

                string[] parts = candidate.Split('.');

                // Four-component versions are accepted and the revision ignored: AppInfo falls back
                // to Assembly.GetName().Version, which always renders as major.minor.build.revision,
                // and rejecting it would make every comparison fail — reported as "up to date".
                if (parts.Length >= 1)
                {
                    int major = 0;
                    int minor = 0;
                    int patch = 0;
                    bool componentsValid = TryParseComponent(parts, 0, ref major)
                        && TryParseComponent(parts, 1, ref minor)
                        && TryParseComponent(parts, 2, ref patch);

                    if (componentsValid)
                    {
                        version = new SemanticVersion(major, minor, patch);
                        parsed = true;
                    }

                }

            }

            return parsed;
        }

        public int CompareTo(SemanticVersion? other)
        {
            int result;

            if (other is null)
            {
                result = 1;
            }
            else if (Major != other.Major)
            {
                result = Major.CompareTo(other.Major);
            }
            else if (Minor != other.Minor)
            {
                result = Minor.CompareTo(other.Minor);
            }
            else
            {
                result = Patch.CompareTo(other.Patch);
            }

            return result;
        }

        private static bool TryParseComponent(string[] parts, int index, ref int value)
        {
            bool valid;

            if (index >= parts.Length)
            {
                value = 0;
                valid = true;
            }
            else
            {
                valid = int.TryParse(parts[index], out int parsedValue) && parsedValue >= 0;

                if (valid)
                {
                    value = parsedValue;
                }

            }

            return valid;
        }
    }
}
