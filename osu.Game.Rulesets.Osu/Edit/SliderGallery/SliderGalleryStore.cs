// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.Osu.Edit.SliderGallery
{
    /// <summary>
    /// The gallery's shapes, shared by all maps: one file per shape in <c>sliders/</c> of the data directory.
    /// </summary>
    public class SliderGalleryStore
    {
        public const string DIRECTORY = "sliders";

        public BindableList<SliderShape> Shapes { get; } = new BindableList<SliderShape>();

        private readonly Storage storage;

        public SliderGalleryStore(Storage gameStorage)
        {
            storage = gameStorage.GetStorageForDirectory(DIRECTORY);
            Reload();
        }

        /// <summary>
        /// Re-reads all shapes, for files added or edited by hand.
        /// </summary>
        public void Reload()
        {
            var shapes = storage.GetFiles(string.Empty, "*.txt").Select(read).OfType<SliderShape>().OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

            Shapes.Clear();
            Shapes.AddRange(shapes);
        }

        /// <summary>
        /// Writes the shape to its file, adding it to the gallery if it's new.
        /// </summary>
        public void Save(SliderShape shape)
        {
            try
            {
                using (var stream = storage.CreateFileSafely(fileName(shape)))
                using (var writer = new StreamWriter(stream))
                    SliderShapeSerializer.Write(shape, writer);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to save slider shape \"{shape.Name}\"");
                return;
            }

            if (!Shapes.Contains(shape))
                Shapes.Add(shape);
        }

        public void Delete(SliderShape shape)
        {
            try
            {
                storage.Delete(fileName(shape));
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to delete slider shape \"{shape.Name}\"");
            }

            Shapes.Remove(shape);
        }

        public void PresentExternally() => storage.PresentExternally();

        private static string fileName(SliderShape shape) => $"{shape.ID}.txt";

        private SliderShape? read(string path)
        {
            try
            {
                using (var stream = storage.GetStream(path))
                using (var reader = new StreamReader(stream))
                {
                    var shape = SliderShapeSerializer.Read(reader);

                    // the file name is what identifies a shape on disk; a copied file keeps its old id inside.
                    if (shape != null && Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var id))
                        shape.ID = id;

                    return shape;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to load slider shape {path}");
                return null;
            }
        }
    }
}
