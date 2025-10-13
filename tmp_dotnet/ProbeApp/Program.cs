using System;
using OpenAI.Embeddings;

class Program
{
    static void Main()
    {
        foreach (var iface in typeof(OpenAIEmbeddingCollection).GetInterfaces())
        {
            Console.WriteLine(iface.FullName);
        }
    }
}
