namespace Deskrig.Core.Display;

public sealed record DisplayApplyResult(bool Success, IReadOnlyList<string> MissingDisplayNames);
