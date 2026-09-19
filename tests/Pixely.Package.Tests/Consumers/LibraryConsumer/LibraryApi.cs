using Pixely;

namespace LibraryConsumer;

public static class LibraryApi
{
    public static PixelyException CreateException() => new("library built on the Pixely SDK");
}
