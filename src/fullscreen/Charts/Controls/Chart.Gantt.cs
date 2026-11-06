using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Collections;

namespace fullscreen.Charts;

public partial class Chart {
    /// <summary>Gets absolute task intervals. Input order determines independent lanes.</summary>
    [Bindable]
    public BindableList<ChartTask> Tasks { get; }

    /// <summary>Gets or sets the first visible task row; Up/Down and PageUp/PageDown scroll lanes.</summary>
    [Bindable]
    public partial int TaskOffset { get; set; }

    private int _taskVersion = -1;
    private ChartSchedule? _schedule;
}
