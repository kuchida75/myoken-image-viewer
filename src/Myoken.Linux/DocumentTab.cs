using Avalonia.Controls;

namespace Myoken.Linux;

// Stable in-process identity, separate from its header and selected-content presenter.
// A null path denotes Browser. Persisted sessions still contain paths only.
internal sealed class DocumentTab
{
    public string? Path { get; set; }
    public required Control Content { get; init; }
}
