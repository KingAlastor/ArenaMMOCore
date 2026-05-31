using System.Runtime.InteropServices;

namespace SharedLibrary
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayerMovementCommand
    {
        public int PlayerId;
        public float TargetX;
        public float TargetY;
    }
}
