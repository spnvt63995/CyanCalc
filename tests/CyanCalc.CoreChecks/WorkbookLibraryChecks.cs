using CyanCalc;
using CyanCalc.Desktop;

internal static class WorkbookLibraryChecks
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "cyancalc-library-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        try
        {
            Guid original, firstCopy, secondCopy;
            using (var library = new WorkbookLibrary(root))
            {
                using var model = new UserModel("Library test");
                model.SetUserInput(0, 1, 1, "42");
                model.SetUserInput(0, 1, 2, "=A1*2");
                original = library.Insert(model).Id;
                library.TogglePin(original);
                firstCopy = library.Duplicate(original).Id;
                secondCopy = library.Duplicate(original).Id;
                Check(library.Settings.Selected == original, "Duplicate must not switch the selected workbook");
                Check(library.Entries.Single(e => e.Id == firstCopy).Name == "Library test (1)", "First copy name");
                Check(library.Entries.Single(e => e.Id == secondCopy).Name == "Library test (2)", "Duplicate name collision");
                model.SetName("Renamed"); model.SetUserInput(0, 1, 1, "7"); library.Save(original, model);
                library.SetPreferences(library.Settings with { Language = "en-GB", Appearance = "dark", NoticeDismissed = true });
                try { using var duplicateHost = new WorkbookLibrary(root); throw new InvalidOperationException("Concurrent host acquired the library"); }
                catch (IOException) { }
            }
            using (var reopened = new WorkbookLibrary(root))
            {
                Check(reopened.StartupSelection == original, "Selected workbook survives restart");
                Check(reopened.Settings.Appearance == "dark" && reopened.Settings.NoticeDismissed, "Preferences survive restart");
                var metadata = reopened.Entries.Single(e => e.Id == original);
                Check(metadata.Name == "Renamed" && metadata.Pinned, "Rename and pin survive save/restart");
                using var saved = reopened.Load(original);
                using var copy = reopened.Load(firstCopy);
                Check(saved.Cell(0, 1, 2).Display == "14", "Saved formula result");
                Check(copy.Cell(0, 1, 2).Display == "84", "Duplicate is an independent snapshot");

                // Force the metadata commit to fail. The previous .ic must remain
                // readable, and the failed save must not replace the cache entry.
                var path = Path.Combine(root, original.ToString("N") + ".workbook.json");
                var metadataBytes = File.ReadAllBytes(path);
                File.Delete(path); Directory.CreateDirectory(path);
                saved.SetUserInput(0, 1, 1, "100");
                try { reopened.Save(original, saved); throw new InvalidOperationException("Obstructed commit succeeded"); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Directory.Delete(path); File.WriteAllBytes(path, metadataBytes);
                using var preserved = reopened.Load(original);
                Check(preserved.Cell(0, 1, 2).Display == "14", "Failed save must preserve previous workbook bytes");

                reopened.Delete(firstCopy);
                Check(reopened.StartupSelection == original, "Deleting another workbook does not switch selection");
                reopened.Delete(original);
                Check(reopened.StartupSelection == secondCopy, "Deleting current workbook selects newest remaining");
                reopened.Delete(secondCopy);
                Check(reopened.StartupSelection is null && reopened.Entries.Count == 0, "Deleting the last workbook leaves a fresh-start state");
            }
            // Damaged metadata must be surfaced, never silently replaced with an
            // empty library. Startup can report the error without destroying data.
            var damaged = Path.Combine(root, Guid.NewGuid().ToString("N") + ".workbook.json");
            File.WriteAllText(damaged, "{broken");
            try { using var invalid = new WorkbookLibrary(root); throw new InvalidOperationException("Damaged metadata was ignored"); }
            catch (System.Text.Json.JsonException) { }
            Check(File.ReadAllText(damaged) == "{broken", "Damaged metadata must be preserved");
        }
        finally { Directory.Delete(root, true); }
    }
}
