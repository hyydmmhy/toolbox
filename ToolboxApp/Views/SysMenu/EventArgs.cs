using System;

namespace SysMenu
{
    [Serializable]
    public class EventArgs<T> : EventArgs
    {
        public T Entity { get; }

        public EventArgs(T entity)
        {
            Entity = entity;
        }
    }
}
