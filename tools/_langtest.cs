class T {
    static void Main() {
        string s = "x";
        int? n = s?.Length;
        System.Console.WriteLine($"interp={n} name={nameof(s)}");
    }
}
