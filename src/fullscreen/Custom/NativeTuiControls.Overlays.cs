using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Custom;

internal static partial class NativeTuiControls {
    internal static bool IsOverlay(Visual visual) =>
        visual is Popup or Dialog or CommandPalette or Toast or SearchReplacePopup;
    private sealed class NativeLogSearchTarget(Func<LogControl> find) : ISearchReplaceTarget {
        private LogControl Log => find();
        public string Title => "Search log";
        public bool SupportsReplace => false;

        public void SetQuery(in SearchQuery query) {
            Log.SearchCaseSensitive = query.CaseSensitive;
            Log.SearchWholeWord = query.WholeWord;
            Log.SearchRegex = query.UseRegex;
            Log.Search(query.Text ?? "");
        }

        public void NextMatch() =>
            Log.GoToNextMatch();
        public void PreviousMatch() =>
            Log.GoToPreviousMatch();
        public int ReplaceCurrent(string replacement) =>
            0;
        public int ReplaceAll(string replacement) =>
            0;
        public string GetStatusText() =>
            Log.MatchCount + " matches";
        public string? GetErrorText() =>
            null;
    }
}
