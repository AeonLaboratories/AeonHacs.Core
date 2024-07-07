using System;

namespace AeonHacs.Utilities
{
    // This class creates a nullable, reference-type object that
    // provides referrable get/set functionality for a non-reference-type
    // object (such as an int).
    public sealed class Ref<T>
    {
        private readonly Func<T> getter;
        private readonly Action<T> setter;
        public Ref(Func<T> getter, Action<T> setter)
        {
            this.getter = getter;
            this.setter = setter;
        }
        public T Value { get { return getter(); } set { setter(value); } }
    }

    /* usage example
    void M()
    {
        int y = 123;
        Ref<int> x = new Ref<int>(()=>y, v=>{y=v;});
        int z = x.Value;        // now, z == 123
        x.Value = 456;            // now, y == 456
    }
    */
}
