// Phase 4: extracted from MainWindow.xaml.cs. Behaviour is unchanged;
// this file only groups one responsibility so the code stays maintainable.

namespace Instageram;

public sealed class Summary
{
    public long Projects { get; set; }
    public long Campaigns { get; set; }
    public long Ù‡Ø¯Ù { get; set; }
    public long Current { get; set; }
    public long Target { get; set; }
    public double Progress => Ù‡Ø¯Ù == 0 ? 0 : Math.Min(100, Current * 100.0 / Ù‡Ø¯Ù);
}
