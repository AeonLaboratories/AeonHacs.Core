namespace AeonHacs.Utilities
{
    // The dynamic casting gyrations here are necessary because C#
    // can't guarantee that any given operator is defined for T, and
    // doesn't provide a way to constrain generic classes to
    // those types which *do* define it.
    // It would be good to benchmark this solution against the
    // equivalent type-specific Utility.EvaulatePolynomial() functions.
    public class Polynomial<T> where T : struct
    {
        public T[] Coefficients { get; set; }

        public Polynomial(T[] coefficients)
        { Coefficients = coefficients; }

        public T Evaluate(T x)
        {
            int i = Coefficients.Length;
            T sum = Coefficients[--i];
            while (i > 0)
                sum = (T)((dynamic)x * (dynamic)sum + (dynamic)Coefficients[--i]);
            return sum;
        }
    }

}
