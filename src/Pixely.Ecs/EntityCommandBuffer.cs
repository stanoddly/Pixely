using Pixely.Collections;

namespace Pixely.Ecs;

// Records structural changes during a query and applies them in order afterwards.
public sealed class EntityCommandBuffer
{
    private enum CommandKind
    {
        Add,
        Remove,
        Destroy
    }

    private readonly record struct Command(CommandKind Kind, Entity Entity, ComponentCommands? Components, int ValueIndex);

    private abstract class ComponentCommands
    {
        public abstract void Add(World world, Entity entity, int valueIndex);

        public abstract void Remove(World world, Entity entity);

        public abstract void Clear();
    }

    // Values stay typed per component, so recording an Add does not box.
    private sealed class ComponentCommands<T> : ComponentCommands where T : struct
    {
        public FastList<T> Values { get; } = new FastList<T>(4);

        public override void Add(World world, Entity entity, int valueIndex) => world.Add(entity, Values[valueIndex]);

        public override void Remove(World world, Entity entity) => world.Remove<T>(entity);

        // Resize keeps the old values in the array, so clear them first to release what they reference.
        public override void Clear()
        {
            Values.AsSpan().Clear();
            Values.Resize(0);
        }
    }

    private readonly FastList<Command> _commands = new FastList<Command>();
    private readonly FastList<ComponentCommands?> _componentCommands = new FastList<ComponentCommands?>(4);

    public void Add<T>(Entity entity, T component) where T : struct
    {
        ComponentCommands<T> components = GetComponentCommands<T>();
        _commands.Add(new Command(CommandKind.Add, entity, components, components.Values.Add(component)));
    }

    public void Remove<T>(Entity entity) where T : struct
    {
        _commands.Add(new Command(CommandKind.Remove, entity, GetComponentCommands<T>(), 0));
    }

    public void Destroy(Entity entity)
    {
        _commands.Add(new Command(CommandKind.Destroy, entity, null, 0));
    }

    // Clears the buffer even when a command throws, so a retry does not apply the commands before it twice.
    public void Apply(World world)
    {
        try
        {
            foreach (Command command in _commands.AsReadOnlySpan())
            {
                switch (command.Kind)
                {
                    case CommandKind.Add:
                        command.Components!.Add(world, command.Entity, command.ValueIndex);
                        break;
                    case CommandKind.Remove:
                        command.Components!.Remove(world, command.Entity);
                        break;
                    case CommandKind.Destroy:
                        world.Destroy(command.Entity);
                        break;
                }
            }
        }
        finally
        {
            _commands.Resize(0);
            foreach (ComponentCommands? components in _componentCommands.AsReadOnlySpan())
            {
                components?.Clear();
            }
        }
    }

    private ComponentCommands<T> GetComponentCommands<T>() where T : struct
    {
        int id = ComponentType<T>.Id;
        if (id >= _componentCommands.Length)
        {
            _componentCommands.Resize(id + 1);
        }

        ref ComponentCommands? components = ref _componentCommands[id];
        components ??= new ComponentCommands<T>();
        return (ComponentCommands<T>)components;
    }
}
