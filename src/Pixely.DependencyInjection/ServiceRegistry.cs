using System.Collections;

namespace Pixely.DependencyInjection;

public sealed class ServiceRegistry<TService> : IEnumerable<TService>
    where TService : class
{
    private readonly Func<TService, int>? _orderKey;
    private readonly List<TService> _services = new();
    private readonly List<TService> _pendingAdditions = new();
    private int _activeEnumerationCount;

    public ulong Version { get; private set; }

    internal ServiceRegistry(Func<TService, int>? orderKey)
    {
        _orderKey = orderKey;
    }

    public Enumerator GetEnumerator()
    {
        if (_activeEnumerationCount == 0)
        {
            PrepareEnumeration();
        }

        return new Enumerator(this);
    }

    IEnumerator<TService> IEnumerable<TService>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    internal void Register(TService service)
    {
        for (int i = 0; i < _services.Count; i++)
        {
            if (ReferenceEquals(_services[i], service))
            {
                return;
            }
        }

        for (int i = 0; i < _pendingAdditions.Count; i++)
        {
            if (ReferenceEquals(_pendingAdditions[i], service))
            {
                return;
            }
        }

        _pendingAdditions.Add(service);
    }

    internal void Unregister(TService service)
    {
        for (int i = 0; i < _pendingAdditions.Count; i++)
        {
            if (ReferenceEquals(_pendingAdditions[i], service))
            {
                _pendingAdditions.RemoveAt(i);
                return;
            }
        }

        for (int i = 0; i < _services.Count; i++)
        {
            if (!ReferenceEquals(_services[i], service))
            {
                continue;
            }

            if (_activeEnumerationCount > 0)
            {
                _services[i] = null!;
            }
            else
            {
                _services.RemoveAt(i);
            }
            Version++;
            return;
        }
    }

    private void PrepareEnumeration()
    {
        if (_pendingAdditions.Count > 0)
        {
            _services.AddRange(_pendingAdditions);
            _pendingAdditions.Clear();

            if (_orderKey != null)
            {
                _services.StableSort(_orderKey);
            }

            Version++;
        }
    }

    private void FinishEnumeration()
    {
        _activeEnumerationCount--;
        if (_activeEnumerationCount > 0)
        {
            return;
        }

        int destination = 0;
        for (int source = 0; source < _services.Count; source++)
        {
            TService? service = _services[source];
            if (service is null)
            {
                continue;
            }

            if (destination != source)
            {
                _services[destination] = service;
            }

            destination++;
        }

        if (destination < _services.Count)
        {
            _services.RemoveRange(destination, _services.Count - destination);
        }
    }

    public struct Enumerator : IEnumerator<TService>
    {
        private ServiceRegistry<TService>? _registry;
        private readonly int _count;
        private int _index;
        private TService? _current;

        internal Enumerator(ServiceRegistry<TService> registry)
        {
            _registry = registry;
            _count = registry._services.Count;
            _index = -1;
            _current = null;
            registry._activeEnumerationCount++;
        }

        public TService Current => _current!;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            ServiceRegistry<TService>? registry = _registry;
            if (registry is null)
            {
                return false;
            }

            while (++_index < _count)
            {
                TService? service = registry._services[_index];
                if (service is null)
                {
                    continue;
                }

                _current = service;
                return true;
            }

            _current = null;
            return false;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
            ServiceRegistry<TService>? registry = _registry;
            if (registry is null)
            {
                return;
            }

            _registry = null;
            _current = null;
            registry.FinishEnumeration();
        }
    }
}
