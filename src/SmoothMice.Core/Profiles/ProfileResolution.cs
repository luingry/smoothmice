using SmoothMice.Core.Config;

namespace SmoothMice.Core.Profiles;

public readonly record struct ProfileResolution(ScrollProfileSettings Settings, bool InterceptForSmoothing);
