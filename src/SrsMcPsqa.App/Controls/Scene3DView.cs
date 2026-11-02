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

    /// <summary>HelixToolkit 3D overview in patient coordinates (mm), viewed from anterior with superior up. Pure rendering.</summary>
    public sealed class Scene3DView : UserControl
    {
        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(nameof(Scene), typeof(Scene3D), typeof(Scene3DView),
            new PropertyMetadata(null, (d, _) => ((Scene3DView)d).Rebuild()));

        public Scene3D? Scene { get => (Scene3D?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }

        private readonly HelixViewport3D _viewport;
        private readonly ModelVisual3D _content = new ModelVisual3D();
        private bool _cameraSet;

        public Scene3DView()
        {
            _viewport = new HelixViewport3D
            {
                ShowCoordinateSystem = true,
                ShowViewCube = true,
                ZoomExtentsWhenLoaded = false,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)),
                IsHeadLightEnabled = true,
            };
            _viewport.Children.Add(new DefaultLights());
            _viewport.Children.Add(_content);
            Content = _viewport;
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
                var mat = MaterialHelper.CreateMaterial(colour);
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

            if (!_cameraSet && (s.Meshes.Count > 0 || s.Planes.Count > 0))
            {
                var all = s.Meshes.SelectMany(m => m.Mesh.Positions).Concat(s.Planes.Select(p => p.Centre)).ToList();
                var centre = new Point3D(all.Average(p => p.X), all.Average(p => p.Y), all.Average(p => p.Z));
                // Anterior view: look along +Y (posterior) with +Z (superior) up, so patient left (+X) is on the right.
                _viewport.Camera = new PerspectiveCamera(centre + new Vector3D(0, -350, 0), new Vector3D(0, 350, 0), new Vector3D(0, 0, 1), 45);
                _cameraSet = true;
            }
        }
    }
}
