namespace Pixely.Input;

// The cursor service that created a cursor and destroys it when the cursor is disposed.
internal interface ICursorOwner
{
    void Destroy(Cursor cursor);
}
