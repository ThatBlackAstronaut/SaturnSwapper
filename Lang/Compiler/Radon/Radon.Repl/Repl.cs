using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Radon.Repl;

internal abstract class Repl
{
    private readonly List<string> _submissionHistory = new();
    protected int _submissionHistoryIndex;
    private bool _done;

    public void Run()
    {
        while (true)
        {
            var text = EditSubmission();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!text.Contains(Environment.NewLine) && text.StartsWith("?"))
            {
                EvaluateMetaCommand(text);
            }
            else
            {
                EvaluateSubmission(text);
            }

            _submissionHistory.Add(text);
            _submissionHistoryIndex = 0;
        }
    }

    public void RunOnce(string text)
    {
        var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            RenderLine(line);
            Console.WriteLine();
        }
    }

    private sealed class SubmissionView
    {
        private readonly Action<string> _lineRenderer;
        private readonly ObservableCollection<string> _submissionDocument;
        private readonly int _cursorTop;
        private int _renderedLineCount;
        private int _currentLine;
        private int _currentCharacter;

        public SubmissionView(Action<string> lineRenderer, ObservableCollection<string> submissionDocument)
        {
            _lineRenderer = lineRenderer;
            _submissionDocument = submissionDocument;
            submissionDocument.CollectionChanged += SubmissionDocumentChanged;
            _cursorTop = Console.CursorTop;
            Render();
        }

        private void SubmissionDocumentChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Render();
        }

        private void Render()
        {
            Console.CursorVisible = false;

            var lineCount = 0;

            foreach (var line in _submissionDocument)
            {
                Console.SetCursorPosition(0, _cursorTop + lineCount);
                Console.ForegroundColor = ConsoleColor.Green;

                if (lineCount == 0)
                {
                    Console.Write("» ");
                }
                else
                {
                    Console.Write("· ");
                }

                Console.ResetColor();
                _lineRenderer(line);
                Console.WriteLine(new string(' ', Console.WindowWidth - line.Length));
                lineCount++;
            }

            var numberOfBlankLines = _renderedLineCount - lineCount;
            if (numberOfBlankLines > 0)
            {
                string blankLine = new(' ', Console.WindowWidth);
                for (var i = 0; i < numberOfBlankLines; i++)
                {
                    Console.SetCursorPosition(0, _cursorTop + lineCount + i);
                    Console.WriteLine(blankLine);
                }
            }

            _renderedLineCount = lineCount;
            Console.CursorVisible = true;
            UpdateCursorPosition();
        }

        private void UpdateCursorPosition()
        {
            Console.CursorTop = _cursorTop + _currentLine;
            Console.CursorLeft = 2 + CurrentCharacter;
        }

        public int CurrentLine
        {
            get => _currentLine;
            set
            {
                if (_currentLine != value)
                {
                    _currentLine = value;
                    _currentCharacter = Math.Min(_submissionDocument[_currentLine].Length, _currentCharacter);
                    UpdateCursorPosition();
                }
            }
        }

        public int CurrentCharacter
        {
            get => _currentCharacter;
            set
            {
                if (_currentCharacter != value)
                {
                    _currentCharacter = value;
                    UpdateCursorPosition();
                }
            }
        }
    }

    private string EditSubmission()
    {
        _done = false;

        var document = new ObservableCollection<string> { string.Empty };
        var view = new SubmissionView(RenderLine, document);

        while (!_done)
        {
            var key = Console.ReadKey(true);
            HandleKey(key, document, view);
        }

        view.CurrentLine = document.Count - 1;
        view.CurrentCharacter = document[view.CurrentLine].Length;
        Console.WriteLine();

        return string.Join(Environment.NewLine, document);
    }

    private void HandleKey(ConsoleKeyInfo key, ObservableCollection<string> document, SubmissionView view)
    {
        if (key.Modifiers == default)
        {
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    HandleEscape(document, view);
                    break;
                case ConsoleKey.Enter:
                    HandleEnter(document, view);
                    break;
                case ConsoleKey.LeftArrow:
                    HandleLeftArrow(view);
                    break;
                case ConsoleKey.RightArrow:
                    HandleRightArrow(document, view);
                    break;
                case ConsoleKey.UpArrow:
                    HandleUpArrow(view);
                    break;
                case ConsoleKey.DownArrow:
                    HandleDownArrow(document, view);
                    break;
                case ConsoleKey.Backspace:
                    HandleBackspace(document, view);
                    break;
                case ConsoleKey.Delete:
                    HandleDelete(document, view);
                    break;
                case ConsoleKey.Home:
                    HandleHome(view);
                    break;
                case ConsoleKey.End:
                    HandleEnd(document, view);
                    break;
                case ConsoleKey.Tab:
                    HandleTab(document, view);
                    break;
                case ConsoleKey.PageUp:
                    HandlePageUp(document, view);
                    break;
                case ConsoleKey.PageDown:
                    HandlePageDown(document, view);
                    break;
            }
        }
        else if (key.Modifiers == ConsoleModifiers.Control)
        {
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    HandleControlEnter(document, view);
                    break;
            }
        }

        if (key.KeyChar >= ' ')
        {
            HandleTyping(document, view, key.KeyChar.ToString());
        }
    }

    private static void HandleEscape(ObservableCollection<string> document, SubmissionView view)
    {
        document[view.CurrentLine] = string.Empty;
        view.CurrentCharacter = 0;
    }

    private void HandleEnter(ObservableCollection<string> document, SubmissionView view)
    {
        var submissionText = string.Join(Environment.NewLine, document);
        if (submissionText.StartsWith("?") || IsCompleteSubmission(submissionText))
        {
            _done = true;
            return;
        }

        InsertLine(document, view);
    }

    private static void HandleControlEnter(ObservableCollection<string> document, SubmissionView view)
    {
        InsertLine(document, view);
    }

    private static void InsertLine(ObservableCollection<string> document, SubmissionView view)
    {
        var remainder = document[view.CurrentLine][view.CurrentCharacter..];
        document[view.CurrentLine] = document[view.CurrentLine][..view.CurrentCharacter];

        var lineIndex = view.CurrentLine + 1;
        document.Insert(lineIndex, remainder);
        view.CurrentCharacter = 0;
        view.CurrentLine = lineIndex;
    }

    private static void HandleLeftArrow(SubmissionView view)
    {
        if (view.CurrentCharacter > 0)
        {
            view.CurrentCharacter--;
        }
    }

    private static void HandleRightArrow(ObservableCollection<string> document, SubmissionView view)
    {
        var line = document[view.CurrentLine];
        if (view.CurrentCharacter <= line.Length - 1)
        {
            view.CurrentCharacter++;
        }
    }

    private static void HandleUpArrow(SubmissionView view)
    {
        if (view.CurrentLine > 0)
        {
            view.CurrentLine--;
        }
    }

    private static void HandleDownArrow(ObservableCollection<string> document, SubmissionView view)
    {
        if (view.CurrentLine < document.Count - 1)
        {
            view.CurrentLine++;
        }
    }

    private static void HandleBackspace(ObservableCollection<string> document, SubmissionView view)
    {
        var start = view.CurrentCharacter;
        if (start == 0)
        {
            if (view.CurrentLine == 0)
            {
                return;
            }

            var currentLine = document[view.CurrentLine];
            var previousLine = document[view.CurrentLine - 1];
            document.RemoveAt(view.CurrentLine);
            view.CurrentLine--;
            document[view.CurrentLine] = previousLine + currentLine;
            view.CurrentCharacter = previousLine.Length;
        }
        else
        {
            var lineIndex = view.CurrentLine;
            var line = document[lineIndex];
            var before = line[..(start - 1)];
            var after = line[start..];
            document[lineIndex] = before + after;
            view.CurrentCharacter--;
        }
    }

    private static void HandleDelete(ObservableCollection<string> document, SubmissionView view)
    {
        var lineIndex = view.CurrentLine;
        var line = document[lineIndex];
        var start = view.CurrentCharacter;
        if (start >= line.Length)
        {
            if (view.CurrentLine == document.Count - 1)
            {
                return;
            }

            var nextLine = document[view.CurrentLine + 1];
            document[view.CurrentLine] += nextLine;
            document.RemoveAt(view.CurrentLine + 1);
            return;
        }

        var before = line[..start];
        var after = line[(start + 1)..];
        document[lineIndex] = before + after;
    }

    private static void HandleHome(SubmissionView view)
    {
        view.CurrentCharacter = 0;
    }

    private static void HandleEnd(ObservableCollection<string> document, SubmissionView view)
    {
        view.CurrentCharacter = document[view.CurrentLine].Length;
    }

    private static void HandleTab(ObservableCollection<string> document, SubmissionView view)
    {
        const int TabWidth = 4;
        var start = view.CurrentCharacter;
        var remainingSpaces = TabWidth - start % TabWidth;
        var line = document[view.CurrentLine];
        document[view.CurrentLine] = line.Insert(start, new string(' ', remainingSpaces));
        view.CurrentCharacter += remainingSpaces;
    }

    private void HandlePageUp(ObservableCollection<string> document, SubmissionView view)
    {
        _submissionHistoryIndex--;
        if (_submissionHistoryIndex < 0)
        {
            _submissionHistoryIndex = _submissionHistory.Count - 1;
        }

        UpdateDocumentFromHistory(document, view);
    }

    private void HandlePageDown(ObservableCollection<string> document, SubmissionView view)
    {
        _submissionHistoryIndex++;
        if (_submissionHistoryIndex > _submissionHistory.Count - 1)
        {
            _submissionHistoryIndex = 0;
        }

        UpdateDocumentFromHistory(document, view);
    }

    private void UpdateDocumentFromHistory(ObservableCollection<string> document, SubmissionView view)
    {
        if (_submissionHistory.Count == 0)
        {
            return;
        }

        document.Clear();

        var historyItme = _submissionHistory[_submissionHistoryIndex];
        var lines = historyItme.Split(Environment.NewLine);
        foreach (var line in lines)
        {
            document.Add(line);
        }

        view.CurrentLine = document.Count - 1;
        view.CurrentCharacter = document[view.CurrentLine].Length;
    }

    private static void HandleTyping(ObservableCollection<string> document, SubmissionView view, string text)
    {
        var lineIndex = view.CurrentLine;
        var start = view.CurrentCharacter;
        document[lineIndex] = document[lineIndex].Insert(start, text);
        view.CurrentCharacter += text.Length;
    }

    protected void ClearHistory()
    {
        _submissionHistory.Clear();
    }

    protected virtual void RenderLine(string line)
    {
        Console.Write(line);
    }

    protected virtual void EvaluateMetaCommand(string input)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Invalid command {input}.");
        Console.ResetColor();
        }

        protected abstract void EvaluateSubmission(string text);

        protected abstract bool IsCompleteSubmission(string text);
    }