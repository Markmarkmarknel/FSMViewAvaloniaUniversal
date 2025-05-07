using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using FSMExpress.Common.Document;
using System;
using Avalonia.Layout;

namespace FSMExpress.Controls.FsmCanvas;
public class FsmCanvasControl : Grid
{
    private static readonly Color BG_LIGHT_THEME_COLOR = Color.FromRgb(140, 140, 140);
    private static readonly Color BLACK_GRAY_COLOR = Color.FromRgb(32, 32, 32);
    private static readonly Color BLACK_SOFT_COLOR = Color.FromRgb(50, 50, 50);
    private static readonly Color WHITE_GRAY_COLOR = Color.FromRgb(222, 222, 222);
    private static readonly FontFamily DEFAULT_FONT = new("Segoe UI Bold");

    private readonly Canvas _can;
    private readonly MatrixTransform _mt;
    private readonly TextBlock _instructionsTextBlock;
    private readonly Border _instructionsBorder;
    private Point _lastPosition = new(0, 0);
    private bool _beingDragged = false;

    private FsmDocument? _document;
    private FsmDocumentNode? _selectedNode;
    private bool _showInstructions = true;

    public static readonly DirectProperty<FsmCanvasControl, FsmDocument?> DocumentProperty =
        AvaloniaProperty.RegisterDirect<FsmCanvasControl, FsmDocument?>(nameof(Document), o => o.Document, (o, v) => o.Document = v);
    public static readonly DirectProperty<FsmCanvasControl, FsmDocumentNode?> SelectedNodeProperty =
        AvaloniaProperty.RegisterDirect<FsmCanvasControl, FsmDocumentNode?>(nameof(SelectedNode), o => o.SelectedNode, (o, v) => o.SelectedNode = v);
    public static readonly DirectProperty<FsmCanvasControl, bool> ShowInstructionsProperty =
        AvaloniaProperty.RegisterDirect<FsmCanvasControl, bool>(nameof(ShowInstructions), o => o.ShowInstructions, (o, v) => o.ShowInstructions = v);

    public FsmDocument? Document
    {
        get => _document;
        set
        {
            SetAndRaise(DocumentProperty, ref _document, value);
            
            // Reset canvas position when new document is loaded
            if (value != null)
            {
                _mt.Matrix = Matrix.Identity;
            }
            
            RebuildGraph();
            UpdateInstructions();
        }
    }

    public FsmDocumentNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode is not null)
                _selectedNode.IsSelected = false;

            SetAndRaise(SelectedNodeProperty, ref _selectedNode, value);

            if (_selectedNode is not null)
                _selectedNode.IsSelected = true;
            
            UpdateInstructions();
        }
    }

    public bool ShowInstructions
    {
        get => _showInstructions;
        set
        {
            SetAndRaise(ShowInstructionsProperty, ref _showInstructions, value);
            _instructionsBorder.IsVisible = value;
        }
    }

    public FsmCanvasControl() : base()
    {
        _mt = new MatrixTransform();
        _can = new Canvas()
        {
            RenderTransform = _mt
        };

        // Create instructions overlay
        _instructionsTextBlock = new TextBlock
        {
            Text = "No document loaded. Open an FSM to get started.",
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(10),
            FontSize = 11
        };

        _instructionsBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 0, 10),
            Child = _instructionsTextBlock,
            IsVisible = _showInstructions,
            MaxWidth = 300
        };

        Children.Add(_can);
        Children.Add(_instructionsBorder);
        ClipToBounds = true;

        Background = new SolidColorBrush(BG_LIGHT_THEME_COLOR);

        // Make control focusable so it can receive key events
        Focusable = true;

        PointerPressed += MouseDownCanvas;
        PointerReleased += MouseUpCanvas;
        PointerMoved += MouseMoveCanvas;
        PointerWheelChanged += MouseScrollCanvas;
    }

    // Toggles instruction visibility with a keyboard shortcut (H)
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        
        if (e.Key == Key.H)
        {
            ShowInstructions = !ShowInstructions;
            e.Handled = true;
        }
    }

    private void UpdateInstructions()
    {
        string instructionText;

        if (Document == null)
        {
            instructionText = "No document loaded.\nOpen an FSM to get started.";
        }
        else if (SelectedNode == null)
        {
            instructionText = "➤ Left click: Select a node\n" +
                             "➤ Right click + drag: Pan canvas\n" +
                             "➤ Scroll wheel: Zoom in/out\n" +
                             "➤ Press H to hide/show instructions";
        }
        else
        {
            instructionText = $"Selected: {SelectedNode.Name}\n" +
                             "➤ Left click on empty area: Deselect\n" +
                             "➤ Right click + drag: Pan canvas\n" +
                             "➤ Scroll wheel: Zoom in/out\n" +
                             "➤ Press H to hide/show instructions";
        }

        _instructionsTextBlock.Text = instructionText;
    }

    private void MouseDownCanvas(object? sender, PointerPressedEventArgs e)
    {
        // Request focus to make keyboard shortcuts work
        Focus();
        
        var clickProps = e.GetCurrentPoint(this).Properties;
        if (clickProps.IsLeftButtonPressed)
        {
            SelectedNode = null;
        }
        else if (clickProps.IsRightButtonPressed)
        {
            _lastPosition = e.GetPosition(this);
            _beingDragged = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }
    }

    private void MouseUpCanvas(object? sender, PointerReleasedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        _beingDragged = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void MouseMoveCanvas(object? sender, PointerEventArgs e)
    {
        if (!_beingDragged)
            return;

        var curPos = e.GetPosition(this);
        _mt.Matrix *= Matrix.CreateTranslation(curPos.X - _lastPosition.X, curPos.Y - _lastPosition.Y);
        _lastPosition = curPos;
    }

    private void MouseScrollCanvas(object? sender, PointerWheelEventArgs e)
    {
        var scale = 1 + e.Delta.Y / 10;

        var halfWidth = _can.Bounds.Width / 2;
        var halfHeight = _can.Bounds.Height / 2;

        var curPos = e.GetPosition(this);
        var zoomX = curPos.X - halfWidth;
        var zoomY = curPos.Y - halfHeight;

        _mt.Matrix *= Matrix.CreateTranslation(-zoomX, -zoomY);
        _mt.Matrix *= Matrix.CreateScale(scale, scale);
        _mt.Matrix *= Matrix.CreateTranslation(zoomX, zoomY);
    }

    private void RebuildGraph()
    {
        _can.Children.Clear();
        if (Document is null)
            return;

        var globalTransBrush = new SolidColorBrush(WHITE_GRAY_COLOR);
        foreach (var node in Document.Nodes)
        {
            var bounds = node.Bounds;
            var nodeBrush = new SolidColorBrush(node.IsStart ? Colors.Gold : BLACK_SOFT_COLOR);
            var titleBrush = new SolidColorBrush(node.IsGlobal ? BLACK_GRAY_COLOR : DrawingToAvaloniaColor(node.NodeColor));
            var transBrush = new SolidColorBrush(DrawingToAvaloniaColor(node.TransitionColor));

            var stackPanel = new StackPanel();

            stackPanel.Children.Add(new TextBlock
            {
                Foreground = Brushes.White,
                Background = titleBrush,
                Text = node.Name,
                FontFamily = DEFAULT_FONT,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Center
            });

            if (!node.IsGlobal)
            {
                // todo: this is yucky. maybe we should calculate it from the control itself?
                float ypos = 27f;

                foreach (var transition in node.Transitions)
                {
                    stackPanel.Children.Add(new TextBlock
                    {
                        Foreground = Brushes.DimGray,
                        Background = transBrush,
                        Text = transition.Name,
                        FontFamily = DEFAULT_FONT,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                        TextAlignment = TextAlignment.Center
                    });

                    if (transition.ToNode is not null)
                    {
                        var arrow = FsmCanvasArrow.CreateLineBetweenNodes(node, transition.ToNode, transBrush, ypos);
                        _can.Children.Add(arrow);
                    }

                    ypos += 16;
                }
            }
            else
            {
                if (node.Transitions.Count > 0)
                {
                    var transition = node.Transitions[0];
                    if (transition.ToNode is not null)
                    {
                        var arrow = FsmCanvasArrow.CreateLineBetweenNodes(node, transition.ToNode, globalTransBrush, 0);
                        _can.Children.Add(arrow);
                    }
                }
            }

            var innerBorder = new Border()
            {
                Background = nodeBrush,
                CornerRadius = new CornerRadius(10.5)
            };

            stackPanel.OpacityMask = new VisualBrush(innerBorder);

            var innerBorderGrid = new Grid();
            innerBorderGrid.Children.Add(innerBorder);
            innerBorderGrid.Children.Add(stackPanel);

            var nodeInf = new FsmCanvasNodeInfo(node, nodeBrush);
            // todo: this border should just be its own component
            var border = new Border()
            {
                Child = innerBorderGrid,
                Background = nodeBrush,
                BorderBrush = nodeBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(0),
                Width = node.Bounds.Width
            };

            var borderConverter = new FsmCanvasDocNodeBorderConverter(nodeInf);
            var selectedBinding = new Binding
            {
                Source = node,
                Path = nameof(node.IsSelected),
                Converter = borderConverter
            };
            border.Bind(Border.BackgroundProperty, selectedBinding);
            border.Bind(Border.BorderBrushProperty, selectedBinding);
            border.PointerPressed += (sender, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    return;

                SelectedNode = nodeInf.Node;

                // prevent click through
                e.Handled = true;
            };

            Canvas.SetLeft(border, bounds.X);
            Canvas.SetTop(border, bounds.Y);
            _can.Children.Add(border);
        }
    }

    private static Color DrawingToAvaloniaColor(System.Drawing.Color drawingColor)
    {
        if (drawingColor.A == 0)
            return Colors.Gray;

        return Color.FromArgb(
            drawingColor.A,
            drawingColor.R,
            drawingColor.G,
            drawingColor.B
        );
    }
}
