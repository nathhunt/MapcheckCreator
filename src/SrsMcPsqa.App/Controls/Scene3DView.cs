using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using SrsMcPsqa.Core.Detector;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.App.Controls
{
    /// <summary>What the 3D overview shows: PTV meshes and the array squares.</summary>
    public sealed class Scene3D
    {
        public IReadOnlyList<(string Id, TriangleMesh Mesh, Color Colour)> Meshes { get; set; } = Array.Empty<(string, TriangleMesh, Color)>();
        public IReadOnlyList<(ArrayOrientation Orientation, Vec3 Centre, bool Selected)> Planes { get; set; } = Array.Empty<(ArrayOrientation, Vec3, bool)>();
        public bool ShowDiodes { get; set; }
    }

    /// <summary>
    /// 3D overview in patient coordinates. Orientation is shown by a corner triad labelled in patient directions
    /// (L = +x, P = +y, S = +z; DICOM HFS) and a caption naming the current view.
    /// The preset views (Anterior, Left, Superior) use an orthographic camera, so an array seen face-on lies exactly
    /// over the PTV it cuts (no perspective parallax). Pure rendering.
    /// </summary>
    public sealed class Scene3DView : UserControl
    {
        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(nameof(Scene), typeof(Scene3D), typeof(Scene3DView),
            new PropertyMetadata(null, (d, _) => ((Scene3DView)d).Rebuild()));

        public Scene3D? Scene { get => (Scene3D?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }

        private readonly HelixViewport3D _viewport;
        private readonly ModelVisual3D _content = new ModelVisual3D();
        private Point3D _centre;
        private double _distance = 350;
        private bool _cameraSet;

        public Scene3DView()
        {
            _viewport = new HelixViewport3D
            {
                ShowCoordinateSystem = true,
                CoordinateSystemLabelX = "L",
                CoordinateSystemLabelY = "P",
                CoordinateSystemLabelZ = "S",
                ShowViewCube = false,
                ZoomExtentsWhenLoaded = false,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)),
                IsHeadLightEnabled = true,
            };
            _viewport.Children.Add(new DefaultLights());
            _viewport.Children.Add(_content);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
            buttons.Children.Add(ViewButton("Anterior", () => SetView(View.Anterior)));
            buttons.Children.Add(ViewButton("Left", () => SetView(View.Left)));
            buttons.Children.Add(ViewButton("Superior", () => SetView(View.Superior)));
            buttons.Children.Add(ViewButton("Fit", () => _viewport.ZoomExtents(300)));
            // The first view is set before the control has a size; set it again once it is laid out.
            _viewport.Loaded += (_, __) => { if (_cameraSet) SetView(View.Anterior); };

            _caption = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), FontSize = 12,
                Margin = new Thickness(10, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
            };
            // Any mouse interaction may rotate the view: the caption then points to the direction indicator.
            _viewport.PreviewMouseDown += (_, __) => _caption.Text = "Free view: see the L / P / S direction indicator";

            var hint = new TextBlock
            {
                Text = "Drag to rotate · right-drag to pan · wheel to zoom",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)), FontSize = 11,
                Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left,
            };

            var grid = new Grid();
            grid.Children.Add(_viewport);
            grid.Children.Add(buttons);
            grid.Children.Add(hint);
            grid.Children.Add(_caption);
            Content = grid;
        }

        private static Button ViewButton(string text, Action action)
        {
            var b = new Wpf.Ui.Controls.Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
            b.Click += (_, __) => action();
            return b;
        }

        private enum View { Anterior, Left, Superior }

        private readonly TextBlock _caption;
        private double _width = 200;

        /// <summary>
        /// Preset views, orthographic. Screen-right is look × up:
        /// Anterior (look +y, up +z): patient left on the right. Left (look −x, up +z): posterior on the right.
        /// Superior (look −z, up −y): seen from the head, anterior up, patient right on the right.
        /// </summary>
        private void SetView(View v)
        {
            Vector3D look, up;
            string caption;
            switch (v)
            {
                case View.Left: look = new Vector3D(-1, 0, 0); up = new Vector3D(0, 0, 1); caption = "Left view: anterior on the left, superior up"; break;
                case View.Superior: look = new Vector3D(0, 0, -1); up = new Vector3D(0, -1, 0); caption = "Superior view (from the head): anterior up, patient right on the right"; break;
                default: look = new Vector3D(0, 1, 0); up = new Vector3D(0, 0, 1); caption = "Anterior view: patient right on the left, superior up"; break;
            }
            // OrthographicCamera.Width is the horizontal extent; widen it for wide windows so the full height fits.
            double aspect = _viewport.ActualHeight > 1 ? _viewport.ActualWidth / _viewport.ActualHeight : 1.6;
            double width = _width * Math.Max(1.0, aspect);
            _viewport.Camera = new OrthographicCamera(_centre - look * _distance, look * _distance, up, width);
            _caption.Text = caption;
        }

        private void Rebuild()
        {
            _content.Children.Clear();
            var s = Scene;
            if (s == null) return;

            foreach (var (_, mesh, colour) in s.Meshes)
            {
                var g = new MeshGeometry3D();
                foreach (var p in mesh.Positions) g.Positions.Add(new Point3D(p.X, p.Y, p.Z));
                foreach (var i in mesh.TriangleIndices) g.TriangleIndices.Add(i);
                // Slightly see-through so nested PTVs stay visible.
                var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(190, colour.R, colour.G, colour.B)));
                _content.Children.Add(new ModelVisual3D { Content = new GeometryModel3D(g, mat) { BackMaterial = mat } });
            }

            foreach (var (o, c, selected) in s.Planes)
            {
                var size = o.FromPlane(DiodeArray.Extent, DiodeArray.Extent, 0.8);
                var builder = new MeshBuilder();
                builder.AddBox(new Point3D(c.X, c.Y, c.Z), size.X, size.Y, size.Z);
                var colour = selected ? Color.FromArgb(110, 255, 255, 255) : Color.FromArgb(60, 120, 180, 255);
                var mat = new DiffuseMaterial(new SolidColorBrush(colour));
                _content.Children.Add(new ModelVisual3D { Content = new GeometryModel3D(builder.ToMesh(), mat) { BackMaterial = mat } });

                if (selected && s.ShowDiodes)
                {
                    var pts = new Point3DCollection(DiodeArray.Positions.Select(d =>
                    {
                        var p = c + o.FromPlane(d.U, d.V, 0);
                        return new Point3D(p.X, p.Y, p.Z);
                    }));
                    _content.Children.Add(new PointsVisual3D { Points = pts, Color = Colors.Yellow, Size = 2 });
                }
            }

            var all = s.Meshes.SelectMany(m => m.Mesh.Positions).Concat(s.Planes.Select(p => p.Centre)).ToList();
            if (all.Count == 0) return;
            var min = new Vec3(all.Min(p => p.X), all.Min(p => p.Y), all.Min(p => p.Z));
            var max = new Vec3(all.Max(p => p.X), all.Max(p => p.Y), all.Max(p => p.Z));
            if (s.Planes.Count > 0)
            {
                double h = DiodeArray.HalfExtent;
                min = min - new Vec3(h, h, h); max = max + new Vec3(h, h, h);
            }
            _centre = new Point3D((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
            double r = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)) / 2 + 15;
            _distance = Math.Max(300, r * 6);
            _width = 2.2 * r;

            if (!_cameraSet)
            {
                SetView(View.Anterior);
                _cameraSet = true;
            }
        }
    }
}
