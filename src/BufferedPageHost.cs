using System.Windows.Forms;

namespace AmdNrAssistant;

public sealed class BufferedPageHost : Panel
{
    public BufferedPageHost()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}
