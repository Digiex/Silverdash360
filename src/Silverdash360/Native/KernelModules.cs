using System.Runtime.InteropServices;
using System.Text;
using Silverlight360;

namespace Silverdash360.Native
{
    /// <summary>The kernel's module and symbolic link calls, by ordinal.</summary>
    public static partial class Kernel
    {
        // 0x103: NTSTATUS ObCreateSymbolicLink(PSTRING name, PSTRING target)
        [DllImport("xboxkrnl.exe", EntryPoint = "#259")]
        public static extern int ObCreateSymbolicLink(uint name, uint target);

        // 0x104: NTSTATUS ObDeleteSymbolicLink(PSTRING name)
        [DllImport("xboxkrnl.exe", EntryPoint = "#260")]
        public static extern int ObDeleteSymbolicLink(uint name);

        // 0x195: NTSTATUS XexGetModuleHandle(PCSTR name, PHANDLE)
        [DllImport("xboxkrnl.exe", EntryPoint = "#405")]
        private static extern int XexGetModuleHandle(uint name, uint handle);

        /// <summary>Whether a module, such as Silverdash360.ni.dll, is loaded.</summary>
        public static bool IsModuleLoaded(string name)
        {
            using (NativeBuffer text = new NativeBuffer(Encoding.UTF8.GetBytes(name + "\0")))
            using (NativeBuffer handle = new NativeBuffer(4))
            {
                return XexGetModuleHandle(text.Address, handle.Address) >= 0 && handle.U32(0) != 0;
            }
        }
    }
}
