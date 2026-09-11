using ScreenGrab.Core;
using Xunit;

namespace ScreenGrab.Core.Tests;

/// <summary>
/// Exercises the ported crown-jewel: operation history -> live-annotation projection and undo/redo.
/// These are the invariants the Windows app relied on and must hold identically on every OS.
/// </summary>
public class AnnotationHistoryTests
{
	private static RectAnnotation Rect(int x = 0) => new(x, x, 10, 10);
	private static TextAnnotation Text(string s, int x = 0) => new(x, x, s, 16f);

	[Fact]
	public void Empty_history_projects_nothing()
	{
		var h = new AnnotationHistory();
		Assert.Empty(h.ProjectAnnotations());
		Assert.False(h.CanUndo);
		Assert.False(h.CanRedo);
	}

	[Fact]
	public void Adds_project_in_insertion_order()
	{
		var h = new AnnotationHistory();
		var r = Rect(1);
		var t = Text("hello", 2);
		h.Add(new AddRectOp(r));
		h.Add(new AddTextOp(t));

		Assert.Equal(new IAnnotation[] { r, t }, h.ProjectAnnotations());
	}

	[Fact]
	public void Edit_replaces_in_place_preserving_draw_order()
	{
		var h = new AnnotationHistory();
		var first = Text("first");
		var mid = Rect(5);
		var edited = first with { Text = "edited" };

		h.Add(new AddTextOp(first));
		h.Add(new AddRectOp(mid));
		h.Add(new EditTextOp(first, edited));

		// edited text stays at index 0 (its original position), rect after it.
		Assert.Equal(new IAnnotation[] { edited, mid }, h.ProjectAnnotations());
	}

	[Fact]
	public void Edit_of_missing_original_defensively_appends()
	{
		var h = new AnnotationHistory();
		var ghost = Text("never added");
		var replacement = ghost with { Text = "replacement" };
		h.Add(new EditTextOp(ghost, replacement));

		Assert.Equal(new IAnnotation[] { replacement }, h.ProjectAnnotations());
	}

	[Fact]
	public void Delete_removes_the_target()
	{
		var h = new AnnotationHistory();
		var t = Text("bye");
		h.Add(new AddTextOp(t));
		h.Add(new DeleteTextOp(t));

		Assert.Empty(h.ProjectAnnotations());
	}

	[Fact]
	public void Undo_then_redo_restores_projection()
	{
		var h = new AnnotationHistory();
		var r = Rect(1);
		var t = Text("x", 2);
		h.Add(new AddRectOp(r));
		h.Add(new AddTextOp(t));

		Assert.True(h.Undo());
		Assert.Equal(new IAnnotation[] { r }, h.ProjectAnnotations());
		Assert.True(h.CanRedo);

		Assert.True(h.Redo());
		Assert.Equal(new IAnnotation[] { r, t }, h.ProjectAnnotations());
	}

	[Fact]
	public void Adding_after_undo_clears_the_redo_history()
	{
		var h = new AnnotationHistory();
		h.Add(new AddRectOp(Rect(1)));
		h.Add(new AddRectOp(Rect(2)));
		h.Undo();
		Assert.True(h.CanRedo);

		h.Add(new AddRectOp(Rect(3)));
		Assert.False(h.CanRedo);
		Assert.Equal(0, h.RedoCount);
	}

	[Fact]
	public void Undo_on_empty_history_is_a_noop()
	{
		var h = new AnnotationHistory();
		Assert.False(h.Undo());
		Assert.False(h.Redo());
	}

	[Fact]
	public void Undoing_an_edit_reveals_the_original_text()
	{
		var h = new AnnotationHistory();
		var original = Text("original");
		var edited = original with { Text = "edited" };
		h.Add(new AddTextOp(original));
		h.Add(new EditTextOp(original, edited));

		Assert.Equal(new IAnnotation[] { edited }, h.ProjectAnnotations());
		h.Undo();
		Assert.Equal(new IAnnotation[] { original }, h.ProjectAnnotations());
	}

	[Fact]
	public void Clear_empties_both_stacks()
	{
		var h = new AnnotationHistory();
		h.Add(new AddRectOp(Rect(1)));
		h.Undo();
		h.Clear();

		Assert.Equal(0, h.HistoryCount);
		Assert.Equal(0, h.RedoCount);
		Assert.Empty(h.ProjectAnnotations());
	}
}
