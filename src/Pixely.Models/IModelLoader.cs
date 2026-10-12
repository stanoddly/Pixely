namespace Pixely.Models;

public interface IModelLoader
{
    /// <summary>Loads a binary glTF 2.0 file (<c>.glb</c>) from the content source. The caller owns and disposes the result.</summary>
    /// <exception cref="InvalidDataException">The file is malformed.</exception>
    /// <exception cref="NotSupportedException">The file uses a glTF feature the loader does not support.</exception>
    Model Load(ReadOnlySpan<char> path);
}
