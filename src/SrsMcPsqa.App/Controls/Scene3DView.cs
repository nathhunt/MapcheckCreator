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
    /// 3D overview in patient coordinates, labelled with patient directions (L/R, A/P, S/I) instead of x/y/z.
    /// View buttons: Anterior, Left, Superior, Fit. Pure rendering.
    /// DICOM HFS: +X = Left, +Y = Posterior, +Z = Superior.
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
                ShowCoordinateSystem = false,
                ShowViewCube = false,
                ZoomExtentsWhenLoaded = false,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)),
                IsHeadLightEnabled = true,
            };
            _viewport.Children.Add(new DefaultLights());
            _viewport.Children.Add(_content);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
            buttons.Children.Add(ViewButton("Anterior", () => SetView(new Vector3D(0, 1, 0), new Vector3D(0, 0, 1))));
            buttons.Children.Add(ViewButton("Left", () => SetView(new Vector3D(-1, 0, 0), new Vector3D(0, 0, 1))));
            buttons.Children.Add(ViewButton("Superior", () => SetView(new Vector3D(0, 0, -1), new Vector3D(0, -1, 0))));
            buttons.Children.Add(ViewButton("Fit", () => _viewport.ZoomExtents(300)));

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
            Content = grid;
        }

        private static Button ViewButton(string text, Action action)
        {
            var b = new Wpf.Ui.Controls.Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
            b.Click += (_, __) => action();
            return b;
        }

        /// <summary>Look along <paramref name="look"/> at the scene centre with <paramref name="up"/> up.</summary>
        private void SetView(Vector3D look, Vector3D up)
        {
            look.Normalize();
            _viewport.Camera = new PerspectiveCamera(_centre - look * _distance, look * _distance, up, 45);
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
            _distance = Math.Max(150, r * 3.2);

            // Patient-direction labels on each side of the scene.
            AddLabel("L", _centre + new Vector3D(r, 0, 0));
            AddLabel("R", _centre + new Vector3D(-r, 0, 0));
            AddLabel("P", _centre + new Vector3D(0, r, 0));
            AddLabel("A", _centre + new Vector3D(0, -r, 0));
            AddLabel("S", _centre + new Vector3D(0, 0, r));
            AddLabel("I", _centre + new Vector3D(0, 0, -r));

            if (!_cameraSet)
            {
                SetView(new Vector3D(0, 1, 0), new Vector3D(0, 0, 1)); // anterior view: patient left on screen right
                _cameraSet = true;
            }
        }

        private void AddLabel(string text, Point3D at) =>
            _content.Children.Add(new BillboardTextVisual3D
            {
                Text = text, Position = at, FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), Background = Brushes.Transparent,
            });
    }
}
