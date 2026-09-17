namespace HaruFamily.DependencyCore.GraphKit
{
    using System;

    /// <summary>Severity shared by Core, Tool validation, and Editor presentation.</summary>
    public enum GraphDiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// Stable diagnostic location. Every member is an identifier, never an Editor view or reflection object.
    /// A missing lower-level identifier intentionally falls back to the containing document or focus.
    /// </summary>
    public readonly struct GraphDiagnosticLocation
    {
        public string DocumentId { get; }
        public string FocusId { get; }
        public string NodeId { get; }
        public string TokenId { get; }
        public string FieldPath { get; }

        public GraphDiagnosticLocation(string documentId = null, string focusId = null, string nodeId = null,
            string tokenId = null, string fieldPath = null)
        {
            DocumentId = documentId;
            FocusId = focusId;
            NodeId = nodeId;
            TokenId = tokenId;
            FieldPath = fieldPath;
        }
    }

    /// <summary>Immutable structured validation output. Code is the stable machine identifier; text may change.</summary>
    public sealed class GraphDiagnostic
    {
        public string Code { get; }
        public GraphDiagnosticSeverity Severity { get; }
        public string Message { get; }
        public GraphDiagnosticLocation Location { get; }
        public string Fix { get; }

        public GraphDiagnostic(string code, GraphDiagnosticSeverity severity, string message,
            GraphDiagnosticLocation location = default, string fix = null)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Diagnostic code is required.", nameof(code));
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Diagnostic message is required.", nameof(message));
            Code = code;
            Severity = severity;
            Message = message;
            Location = location;
            Fix = fix;
        }
    }
}
