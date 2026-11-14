// Fixtures run in parallel. A fixture that uses process-wide state, such as real SDL, SDL_ttf or an environment variable, is
// [NonParallelizable], so it runs alone.
[assembly: Parallelizable(ParallelScope.Fixtures)]
