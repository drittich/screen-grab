namespace ScreenGrab.Core;

// Annotations are the drawable items; operations are the undoable steps that produce them.
// Ported verbatim from the WinForms Form1 model (pure logic, no UI types). Geometry is
// expressed with plain ints so the model stays OS- and toolkit-agnostic; the Skia compositor
// converts to SK types at draw time.
public interface IAnnotation { }

/// <summary>A red rounded-rectangle highlight, in image pixel coordinates.</summary>
public sealed record RectAnnotation(int X, int Y, int Width, int Height) : IAnnotation;

/// <summary>A movable text box (white text on a blue background) anchored at (X, Y).</summary>
public sealed record TextAnnotation(int X, int Y, string Text, float FontSize) : IAnnotation;

// Operation history. Replaying all operations oldest->newest yields the live annotation
// set. This keeps the stack append-only so undo/redo of edits is predictable.
public interface IOperation { }

public sealed record AddRectOp(RectAnnotation Annotation) : IOperation;
public sealed record AddTextOp(TextAnnotation Annotation) : IOperation;
public sealed record EditTextOp(TextAnnotation Old, TextAnnotation New) : IOperation;
public sealed record DeleteTextOp(TextAnnotation Target) : IOperation;
