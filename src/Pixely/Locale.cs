namespace Pixely;

// Language is an ISO 639 code such as "cs"; Country is an ISO 3166 code such as "CZ", or null when the locale names none.
public readonly record struct Locale(string Language, string? Country);
