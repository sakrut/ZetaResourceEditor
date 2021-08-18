namespace ZetaResourceEditor.RuntimeBusinessLogic.Snapshots
{
    using BL;
    using DL;
    using ExportImportExcel.Export;
    using Language;
    using Projects;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Data;
    using System.Linq;
    using ZetaLongPaths;

    /// <summary>
    /// 2017-04-02, Uwe Keim:
    /// Damit wir "Use existing translations" nutzen können, hier einen einmaligen Snapshot
    /// vor dem Übersetzen machen.
    /// </summary>
    public sealed class InMemoryTranslationSnapshotController
    {
        private static InMemoryTranslationSnapshot lastSnapshot = null;
        public static InMemoryTranslationSnapshot GetSnapshotInThisSession(Project project,
            string[] languageCodes, string refLanguageCode,
            BackgroundWorker bw)
        {
            refLanguageCode = refLanguageCode.ToLowerInvariant();
            if (lastSnapshot != null)
            {
                if(lastSnapshot.Project.Equals(project)|| project.Name.Equals(lastSnapshot.Project.Name))
                {
                    if (lastSnapshot.LagneCodes.Intersect(languageCodes).Count() == languageCodes.Length && lastSnapshot.RefLanguageCode.Equals(refLanguageCode) )
                    {
                        return lastSnapshot;
                    }
                }
            }
            
            lastSnapshot = new InMemoryTranslationSnapshotController().CreateSnapshot(project, languageCodes,refLanguageCode, bw);
            
            return lastSnapshot;
        }
        public InMemoryTranslationSnapshot  CreateSnapshot(
            Project project,
            string[] languageCodes, string refLanguageCode,
            BackgroundWorker bw)
        {
            var imss = new InMemoryTranslationSnapshot();
            imss.LagneCodes = languageCodes;
            imss.Project = project;
            imss.RefLanguageCode = refLanguageCode;
            project.FileGroups.ForEach(fg => doTakeSnapshot(imss, project, fg, languageCodes,refLanguageCode, bw));

            return imss;
        }

        private void doTakeSnapshot(
            InMemoryTranslationSnapshot imss,
            Project project,
            IGridEditableData fileGroup,
            IEnumerable<string> languageCodes, string refLanguageCode,
            BackgroundWorker bw)
        {
            //var fgi = fileGroup.GetFileByLanguageCode(Project, languageCode);

            var table = new DataProcessing(fileGroup).GetDataTableFromResxFiles(project, true);

            var lcs = new List<string>(languageCodes);
            for (var i = 0; i < lcs.Count; i++)
            {
                lcs[i] = lcs[i].ToLowerInvariant();
            }

            var rowIndex = 0;
            foreach (DataRow row in table.Rows)
            {
                if (rowIndex % 25 == 0 && (bw?.CancellationPending ?? false)) throw new OperationCanceledException();

                var dic = new Dictionary<string, string>();

                string refLangeValue = null;
                for (var sourceColumnIndex = 2;
                    sourceColumnIndex <
                    table.Columns.Count - 1; // Subtract 1, because last column is ALWAYS the comment.
                    ++sourceColumnIndex)
                {
                    var languageValue = row[sourceColumnIndex] as string;
                    var languageCode =
                        ExcelExportController.IsFileName(table.Columns[sourceColumnIndex].ColumnName)
                            ? new LanguageCodeDetection(project)
                                .DetectLanguageCodeFromFileName(
                                    fileGroup.ParentSettings,
                                    table.Columns[sourceColumnIndex].ColumnName)
                            : table.Columns[sourceColumnIndex].ColumnName;
                    languageCode = languageCode.ToLowerInvariant();
                    if (refLanguageCode.Equals(languageCode))
                    {
                        refLangeValue = languageValue;
                    }else if (lcs.Contains(languageCode))
                    {
                        dic[languageCode] = languageValue;
                    }
                }

                imss.AddBatchTranslation(dic,refLangeValue);

                rowIndex++;
            }
        }
    }

    public sealed class InMemoryTranslationSnapshot
    {
        internal Project Project = null;
        internal string[] LagneCodes = null;
        internal string RefLanguageCode;
        private readonly Dictionary<string,InMemoryTranslationSnapshotItem> _items = new();

        /// <summary>
        /// Liefert NULL zurück, falls nicht gefunden.
        /// </summary>
        public string GetTranslation(
            string sourceLanguageCode,
            string destinationLanguageCode,
            string sourceText)
        {
            if (!_items.ContainsKey(sourceText))
                return null;
            
            var one = _items[sourceText].LanguageAndTextPairs.FirstOrDefault(i => i.Key.EqualsNoCase(destinationLanguageCode));
            if (one.Value != null)
            {
                return one.Value;
            }
            return null;
        }

        public void AddBatchTranslation(Dictionary<string, string> languageAndTextPairs, string refLangeValue)
        {
            if(string.IsNullOrWhiteSpace(refLangeValue))
                return;
            if(_items.ContainsKey(refLangeValue))
                return;
            var item = new InMemoryTranslationSnapshotItem(languageAndTextPairs);
            _items.Add(refLangeValue,item);
        }

        public void AddTranslation(
            string sourceLanguageCode,
            string destinationLanguageCode,
            string sourceText,
            string destinationText)
        {
            if(string.IsNullOrWhiteSpace(sourceText))
                return;

            InMemoryTranslationSnapshotItem item;
            if (!_items.ContainsKey(sourceText))
            {
                item = new InMemoryTranslationSnapshotItem(new Dictionary<string, string>());
                _items.Add(sourceText,item);
            }
            else
            {
                item = _items[sourceText];
            }
            item.LanguageAndTextPairs[destinationLanguageCode.ToLowerInvariant()] = destinationText;
        }
    }

    public sealed class InMemoryTranslationSnapshotItem
    {
        public InMemoryTranslationSnapshotItem(Dictionary<string, string> languageAndTextPairs)
        {
            LanguageAndTextPairs = languageAndTextPairs;
        }

        public Dictionary<string, string> LanguageAndTextPairs { get; private set; }

    }
}