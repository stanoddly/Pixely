namespace Pixely;

// Language is usually an ISO 639 code such as "cs"; Country is usually an ISO 3166 code such as "CZ", or null when the locale names none.
// Neither is validated: SDL passes the platform's strings through, so a browser can report a region such as "419".
public readonly record struct Locale(string Language, string? Country);
