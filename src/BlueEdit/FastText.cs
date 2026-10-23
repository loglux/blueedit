using System.Text;

/// <summary>
/// fastText's text classification, prediction only, reading a model file of fastText 0.9 (format version 12) such as its
/// language identification model <c>lid.176.ftz</c>. A port of the parts of fastText (MIT, Facebook) that prediction
/// uses: the dictionary with its character n-grams, the input matrix (plain or product-quantized, as in a .ftz), the
/// average of a text's rows, and the output through hierarchical softmax or softmax. Checked against fastText itself
/// (its WebAssembly build) on 1,621 texts: the same languages in the same order (FastTextTests). Why our own and not a
/// library: docs/tech/proofing-internals.md, "The language of a paragraph".
/// </summary>
sealed class FastText
{
    const int Magic = 793712314, Version = 12;
    const string Eos = "</s>", Bow = "<", Eow = ">", LabelPrefix = "__label__";
    const int LossHs = 1, LossSoftmax = 3;

    readonly int _dim, _minn, _maxn, _bucket, _wordNgrams, _loss, _nwords;
    readonly long _pruneSize;
    readonly Dictionary<int, int> _prune = new();
    readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    readonly string[] _words;
    readonly bool[] _isLabel;
    readonly int[]?[] _subwords;
    readonly IMatrix _input, _output;
    readonly string[] _labels;
    readonly (int Left, int Right)[] _tree = Array.Empty<(int, int)>();

    /// <summary>The labels the model can answer (for lid.176: "en", "ru"... without fastText's "__label__").</summary>
    public IReadOnlyList<string> Labels => _labels;

    public FastText(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (r.ReadInt32() != Magic) throw new InvalidDataException("not a fastText model");
        if (r.ReadInt32() != Version) throw new InvalidDataException("a fastText model of another version than 12");
        // Args: dim, ws, epoch, minCount, neg, wordNgrams, loss, model, bucket, minn, maxn, lrUpdateRate; then t
        var args = new int[12];
        for (int i = 0; i < args.Length; i++) args[i] = r.ReadInt32();
        r.ReadDouble();
        (_dim, _wordNgrams, _loss, _bucket, _minn, _maxn) = (args[0], args[5], args[6], args[8], args[9], args[10]);
        if (_loss is not (LossHs or LossSoftmax)) throw new InvalidDataException($"fastText loss {_loss} is not supported");

        int size = r.ReadInt32();
        _nwords = r.ReadInt32();
        int nlabels = r.ReadInt32();
        r.ReadInt64();                                       // tokens seen in training
        _pruneSize = r.ReadInt64();
        _words = new string[size];
        _isLabel = new bool[size];
        var counts = new long[size];
        var bytes = new List<byte>();
        for (int i = 0; i < size; i++)
        {
            bytes.Clear();
            for (byte b; (b = r.ReadByte()) != 0;) bytes.Add(b);
            _words[i] = Encoding.UTF8.GetString(bytes.ToArray());
            counts[i] = r.ReadInt64();
            _isLabel[i] = r.ReadSByte() == 1;
            _ids.TryAdd(_words[i], i);
        }
        for (long i = 0; i < _pruneSize; i++)
        {
            int from = r.ReadInt32();
            _prune[from] = r.ReadInt32();
        }
        _subwords = new int[]?[_nwords];
        _labels = _words.Skip(_nwords).Take(nlabels)
            .Select(w => w.StartsWith(LabelPrefix, StringComparison.Ordinal) ? w[LabelPrefix.Length..] : w).ToArray();

        _input = r.ReadBoolean() ? new QuantMatrix(r) : new DenseMatrix(r);
        bool qout = r.ReadBoolean();
        _output = _input is QuantMatrix && qout ? new QuantMatrix(r) : new DenseMatrix(r);
        if (_input.Columns != _dim || _output.Columns != _dim) throw new InvalidDataException("fastText matrices do not fit");
        if (_loss == LossHs) _tree = BuildTree(counts.AsSpan(_nwords, nlabels));
    }

    /// <summary>The labels for <paramref name="text"/>, most likely first, with their probabilities; those below 1e-5
    /// are left out, as fastText does. Empty when the text has no word or n-gram the model knows.</summary>
    public List<(string Label, float Probability)> Predict(string text, int k = int.MaxValue)
    {
        var line = InputOf(text);
        var found = new List<(float Score, int Label)>();
        if (line.Count == 0) return new();
        var hidden = new float[_dim];
        foreach (int row in line) _input.AddRow(hidden, row);
        float scale = (float)(1.0 / line.Count);
        for (int i = 0; i < _dim; i++) hidden[i] *= scale;
        if (_loss == LossHs) Walk(_tree.Length - 1, 0f, hidden, found);
        else Softmax(hidden, found);
        return found.OrderByDescending(f => f.Score).Take(k)
            .Select(f => (_labels[f.Label], MathF.Exp(f.Score))).ToList();
    }

    // fastText's predictLine on one line: the words split at ASCII white space, the end of the line as the word "</s>"
    // (its WebAssembly build adds the newline; the plugin's answers, checked against, came through it).
    List<int> InputOf(string text)
    {
        var line = new List<int>();
        var hashes = new List<uint>();
        var tokens = text.Split((char[])[' ', '\n', '\r', '\t', '\v', '\f', '\0'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens.Append(Eos))
        {
            int id = _ids.TryGetValue(token, out int found) ? found : -1;
            bool label = id >= 0 ? _isLabel[id] : token.StartsWith(LabelPrefix, StringComparison.Ordinal);
            if (label) continue;
            if (id >= 0 && id < _nwords) line.AddRange(SubwordsOf(id));
            else if (token != Eos) AddNgrams(line, Bow + token + Eow);
            hashes.Add(Hash(Encoding.UTF8.GetBytes(token)));
        }
        for (int i = 0; i < hashes.Count; i++)
        {
            ulong h = hashes[i];
            for (int j = i + 1; j < hashes.Count && j < i + _wordNgrams; j++)
            {
                h = unchecked(h * 116049371 + hashes[j]);
                Push(line, (int)(h % (ulong)_bucket));
            }
        }
        return line;
    }

    int[] SubwordsOf(int id)
    {
        if (_subwords[id] is { } known) return known;
        var list = new List<int> { id };
        if (_words[id] != Eos) AddNgrams(list, Bow + _words[id] + Eow);
        return _subwords[id] = list.ToArray();
    }

    // The character n-grams of minn to maxn characters (UTF-8 sequences kept whole), not the single "<" or ">"
    void AddNgrams(List<int> line, string word)
    {
        if (_maxn <= 0) return;
        var w = Encoding.UTF8.GetBytes(word);
        for (int i = 0; i < w.Length; i++)
        {
            if ((w[i] & 0xC0) == 0x80) continue;
            int j = i;
            for (int n = 1; j < w.Length && n <= _maxn; n++)
            {
                j++;
                while (j < w.Length && (w[j] & 0xC0) == 0x80) j++;
                if (n >= _minn && !(n == 1 && (i == 0 || j == w.Length)))
                    Push(line, (int)(Hash(w.AsSpan(i, j - i)) % (uint)_bucket));
            }
        }
    }

    void Push(List<int> line, int id)
    {
        if (_pruneSize == 0 || id < 0) return;
        if (_pruneSize > 0)
        {
            if (!_prune.TryGetValue(id, out id)) return;
        }
        line.Add(_nwords + id);
    }

    // FNV-1a over the bytes taken as signed: fastText's hash
    static uint Hash(ReadOnlySpan<byte> s)
    {
        uint h = 2166136261;
        foreach (byte b in s) h = unchecked((h ^ (uint)(sbyte)b) * 16777619);
        return h;
    }

    static float StdLog(float x) => (float)Math.Log(x + 1e-5);

    // Hierarchical softmax: the labels are the leaves of a Huffman tree over their counts
    static (int, int)[] BuildTree(ReadOnlySpan<long> counts)
    {
        int osz = counts.Length;
        var tree = new (int Left, int Right)[2 * osz - 1];
        var count = new long[2 * osz - 1];
        for (int i = 0; i < tree.Length; i++) { tree[i] = (-1, -1); count[i] = (long)1e15; }
        for (int i = 0; i < osz; i++) count[i] = counts[i];
        int leaf = osz - 1, node = osz;
        for (int i = osz; i < 2 * osz - 1; i++)
        {
            int left = leaf >= 0 && count[leaf] < count[node] ? leaf-- : node++;
            int right = leaf >= 0 && count[leaf] < count[node] ? leaf-- : node++;
            tree[i] = (left, right);
            count[i] = count[left] + count[right];
        }
        return tree;
    }

    void Walk(int node, float score, float[] hidden, List<(float, int)> found)
    {
        if (score < StdLog(0)) return;
        var (left, right) = _tree[node];
        if (left == -1 && right == -1)
        {
            found.Add((score, node));
            return;
        }
        float f = _output.DotRow(hidden, node - _labels.Length);
        f = (float)(1.0 / (1 + Math.Exp(-f)));
        Walk(left, score + StdLog(1f - f), hidden, found);
        Walk(right, score + StdLog(f), hidden, found);
    }

    void Softmax(float[] hidden, List<(float, int)> found)
    {
        var output = new float[_output.Rows];
        float max = float.MinValue, sum = 0;
        for (int i = 0; i < output.Length; i++) max = Math.Max(max, output[i] = _output.DotRow(hidden, i));
        for (int i = 0; i < output.Length; i++) sum += output[i] = MathF.Exp(output[i] - max);
        for (int i = 0; i < output.Length; i++)
        {
            float score = StdLog(output[i] / sum);
            if (score >= StdLog(0)) found.Add((score, i));
        }
    }

    interface IMatrix
    {
        int Rows { get; }
        int Columns { get; }
        void AddRow(float[] x, int row);
        float DotRow(float[] x, int row);
    }

    sealed class DenseMatrix : IMatrix
    {
        readonly float[] _data;
        public int Rows { get; }
        public int Columns { get; }

        public DenseMatrix(BinaryReader r)
        {
            Rows = checked((int)r.ReadInt64());
            Columns = checked((int)r.ReadInt64());
            _data = new float[checked(Rows * Columns)];
            r.BaseStream.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_data.AsSpan()));
        }

        public void AddRow(float[] x, int row)
        {
            var d = _data.AsSpan(row * Columns, Columns);
            for (int i = 0; i < d.Length; i++) x[i] += d[i];
        }

        public float DotRow(float[] x, int row)
        {
            var d = _data.AsSpan(row * Columns, Columns);
            float sum = 0;
            for (int i = 0; i < d.Length; i++) sum += x[i] * d[i];
            return sum;
        }
    }

    // A product-quantized matrix (a .ftz): each row is a code per sub-vector, an index into 256 centroids, and
    // optionally a quantized norm it is scaled by
    sealed class QuantMatrix : IMatrix
    {
        readonly bool _qnorm;
        readonly byte[] _codes, _normCodes = Array.Empty<byte>();
        readonly ProductQuantizer _pq, _npq = null!;
        public int Rows { get; }
        public int Columns { get; }

        public QuantMatrix(BinaryReader r)
        {
            _qnorm = r.ReadBoolean();
            Rows = checked((int)r.ReadInt64());
            Columns = checked((int)r.ReadInt64());
            _codes = r.ReadBytes(r.ReadInt32());
            _pq = new ProductQuantizer(r);
            if (_qnorm)
            {
                _normCodes = r.ReadBytes(Rows);
                _npq = new ProductQuantizer(r);
            }
        }

        float Norm(int row) => _qnorm ? _npq.Centroids[_npq.Centroid(0, _normCodes[row])] : 1f;

        public void AddRow(float[] x, int row)
        {
            float norm = Norm(row);
            int code = _pq.Subquantizers * row;
            for (int m = 0, d = _pq.Sub; m < _pq.Subquantizers; m++)
            {
                int c = _pq.Centroid(m, _codes[code + m]);
                if (m == _pq.Subquantizers - 1) d = _pq.LastSub;
                for (int n = 0; n < d; n++) x[m * _pq.Sub + n] += norm * _pq.Centroids[c + n];
            }
        }

        public float DotRow(float[] x, int row)
        {
            float sum = 0;
            int code = _pq.Subquantizers * row;
            for (int m = 0, d = _pq.Sub; m < _pq.Subquantizers; m++)
            {
                int c = _pq.Centroid(m, _codes[code + m]);
                if (m == _pq.Subquantizers - 1) d = _pq.LastSub;
                for (int n = 0; n < d; n++) sum += x[m * _pq.Sub + n] * _pq.Centroids[c + n];
            }
            return sum * Norm(row);
        }
    }

    sealed class ProductQuantizer
    {
        const int Ksub = 256;
        public int Subquantizers { get; }
        public int Sub { get; }
        public int LastSub { get; }
        public float[] Centroids { get; }

        public ProductQuantizer(BinaryReader r)
        {
            int dim = r.ReadInt32();
            Subquantizers = r.ReadInt32();
            Sub = r.ReadInt32();
            LastSub = r.ReadInt32();
            Centroids = new float[dim * Ksub];
            r.BaseStream.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(Centroids.AsSpan()));
        }

        /// <summary>Where centroid <paramref name="i"/> of sub-vector <paramref name="m"/> starts in Centroids.</summary>
        public int Centroid(int m, int i) => m == Subquantizers - 1 ? m * Ksub * Sub + i * LastSub : (m * Ksub + i) * Sub;
    }
}
