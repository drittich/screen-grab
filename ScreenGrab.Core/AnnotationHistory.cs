namespace ScreenGrab.Core;

/// <summary>
/// The append-only operation history and its redo counterpart, plus the projection that turns
/// the history into the ordered set of currently-live annotations. This is the crown jewel of the
/// original WinForms app — ported here verbatim so undo/redo behaves identically on every OS.
/// </summary>
public sealed class AnnotationHistory
{
	private readonly Stack<IOperation> _history = new();
	private readonly Stack<IOperation> _redoStack = new();

	public int HistoryCount => _history.Count;
	public int RedoCount => _redoStack.Count;

	public bool CanUndo => _history.Count > 0;
	public bool CanRedo => _redoStack.Count > 0;

	/// <summary>Records a new operation. Adding one invalidates the redo history.</summary>
	public void Add(IOperation op)
	{
		_history.Push(op);
		_redoStack.Clear();
	}

	/// <summary>Pops the most recent operation onto the redo stack. Returns false if empty.</summary>
	public bool Undo()
	{
		if (_history.Count == 0) return false;
		_redoStack.Push(_history.Pop());
		return true;
	}

	/// <summary>Re-applies the most recently undone operation. Returns false if empty.</summary>
	public bool Redo()
	{
		if (_redoStack.Count == 0) return false;
		_history.Push(_redoStack.Pop());
		return true;
	}

	/// <summary>Clears both stacks (called when a fresh capture starts).</summary>
	public void Clear()
	{
		_history.Clear();
		_redoStack.Clear();
	}

	/// <summary>Projects the operation history into the ordered set of currently-live annotations.</summary>
	public List<IAnnotation> ProjectAnnotations()
	{
		var live = new List<IAnnotation>();
		// _history enumerates newest-first, so reverse to replay oldest-first.
		foreach (IOperation op in _history.Reverse())
		{
			switch (op)
			{
				case AddRectOp ar:
					live.Add(ar.Annotation);
					break;
				case AddTextOp at:
					live.Add(at.Annotation);
					break;
				case EditTextOp et:
					int idx = live.IndexOf(et.Old);
					if (idx >= 0)
						live[idx] = et.New;       // replace in place, preserving draw order
					else
						live.Add(et.New);          // defensive: original missing, just add
					break;
				case DeleteTextOp dt:
					live.Remove(dt.Target);
					break;
			}
		}
		return live;
	}
}
