# Regenerates Mimesis/LangKo.cs from Lang/ko.txt (run from the src folder: python Lang/make_langko.py)
import io
txt = io.open('Lang/ko.txt', encoding='utf-8').read()
cs = ('namespace UnityVRMod.Mimesis\n{\n    /// <summary>Built-in Korean texts (build 48). Generated from Lang/ko.txt - edit that file, then run Lang/make_langko.py.</summary>\n'
      '    internal static class LangKo\n    {\n        public const string Text = @"' + txt.replace('"', '""') + '";\n    }\n}\n')
io.open('Mimesis/LangKo.cs', 'w', encoding='utf-8').write(cs)
print('LangKo.cs:', len(txt), 'chars')
