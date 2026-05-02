using Autodesk.AutoCAD.Runtime;

namespace DDimnotes
{
    public sealed class Entry : IExtensionApplication
    {
        public void Initialize()
        {
            // Language is loaded at the start of each DDimnotes command.
            // Do not read MIS_SET.SET or embedded lang.dat at NETLOAD time.
        }

        public void Terminate()
        {
        }
    }
}
