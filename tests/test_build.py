"""Tests for build.py and the files it produces. Standard library only.

    python tests/test_build.py
"""
import contextlib
import importlib.util
import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


def load_build():
    spec = importlib.util.spec_from_file_location("build", REPO / "build.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


b = load_build()


class Temp(unittest.TestCase):
    """Gives each test a scratch folder and puts build.py's paths back afterwards."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="qudhud-"))
        self.saved = {k: getattr(b, k) for k in ("ROOT", "DIST", "WORKSHOP_JSON", "git", "run_tests")}

    def tearDown(self):
        for k, v in self.saved.items():
            setattr(b, k, v)
        shutil.rmtree(self.tmp, ignore_errors=True)

    def changelog(self, text):
        """Point build.py at a changelog of our own."""
        (self.tmp / "CHANGELOG.md").write_text(text, encoding="utf-8")
        b.ROOT = self.tmp


class VdfValue(unittest.TestCase):
    # steamcmd honours no escape sequences, so a value must never contain a quote or a newline

    def test_never_emits_a_character_that_ends_a_value(self):
        for raw in ['say "hi"', "one\ntwo", "crlf\r\nhere", "tab\there", 'mix"ed\nup']:
            out = b.vdf_value(raw)
            for bad in ('"', "\n", "\r", "\t"):
                self.assertNotIn(bad, out, raw)

    def test_quotes_become_apostrophes(self):
        self.assertEqual(b.vdf_value('the "Linked" light'), "the 'Linked' light")

    def test_whitespace_collapses(self):
        self.assertEqual(b.vdf_value("  a \n\n b\t c  "), "a b c")

    def test_backslashes_are_doubled_like_every_published_example(self):
        self.assertEqual(b.vdf_value(r"C:\Users\M"), r"C:\\Users\\M")


class Changenote(Temp):

    def test_one_list_item_per_entry(self):
        self.changelog("## 9.9.9\n- Fixed a thing.\n- Added another.\n\n## 9.9.8\n- Ignored.\n")
        self.assertEqual(b.changenote("9.9.9"), "v9.9.9[list][*]Fixed a thing.[*]Added another.[/list]")

    def test_missing_full_stop_is_added(self):
        self.changelog("## 9.9.9\n- Fixed a thing\n")
        self.assertEqual(b.changenote("9.9.9"), "v9.9.9[list][*]Fixed a thing.[/list]")

    def test_wrapped_lines_are_kept(self):
        self.changelog("## 9.9.9\n- Fixed a thing that needed\n  some explaining.\n")
        self.assertEqual(b.changenote("9.9.9"),
                         "v9.9.9[list][*]Fixed a thing that needed some explaining.[/list]")

    def test_markdown_is_stripped(self):
        self.changelog("## 9.9.9\n- Fixed `hud_data.js` and **the** thing.\n")
        self.assertEqual(b.changenote("9.9.9"), "v9.9.9[list][*]Fixed hud_data.js and the thing.[/list]")

    def test_empty_section_falls_back_to_the_version(self):
        self.changelog("## 9.9.9\n\n## 9.9.8\n- Old.\n")
        self.assertEqual(b.changenote("9.9.9"), "v9.9.9")

    def test_survives_vdf_escaping_as_one_line(self):
        self.changelog('## 9.9.9\n- The "Linked" light.\n- Another\n  wrapped entry.\n')
        out = b.vdf_value(b.changenote("9.9.9"))
        self.assertNotIn('"', out)
        self.assertNotIn("\n", out)
        self.assertEqual(out.count("[*]"), 2)


class Changelog(unittest.TestCase):

    def test_newest_section_is_the_current_version(self):
        version = (REPO / "VERSION").read_text().strip()
        sections = b.changelog_sections()
        self.assertEqual(sections[0][0], version,
                         "VERSION was bumped without a CHANGELOG section, or the other way round")

    def test_every_section_has_entries(self):
        for version, body in b.changelog_sections():
            self.assertTrue(body.strip(), f"CHANGELOG section {version} is empty")


class Releases(Temp):
    TAGS = "v1.0.1\nv1.0.2\nv1.0.3\nv1.0.6"

    def fake_git(self):
        b.git = lambda *args: self.TAGS if args == ("tag",) else ""

    def test_version_tuple(self):
        self.assertEqual(b.version_tuple("v1.2.3"), (1, 2, 3))
        self.assertIsNone(b.version_tuple("nightly"))

    def test_previous_tag(self):
        self.fake_git()
        self.assertEqual(b.previous_tag("v1.0.7"), "v1.0.6")
        self.assertEqual(b.previous_tag("v1.0.2"), "v1.0.1")
        self.assertIsNone(b.previous_tag("v0.0.1"))

    def test_notes_cover_every_version_since_the_last_tag(self):
        # the case v1.0.6 had to handle by hand: 1.0.4 and 1.0.5 were never released
        self.changelog("## 1.0.6\n- Six.\n\n## 1.0.5\n- Five.\n\n## 1.0.4\n- Four.\n\n## 1.0.3\n- Three.\n")
        notes = b.release_notes("1.0.6", "v1.0.3")
        for v in ("1.0.6", "1.0.5", "1.0.4"):
            self.assertIn(f"### {v}", notes)
        self.assertNotIn("### 1.0.3", notes)
        self.assertIn("## Changes since v1.0.3", notes)
        self.assertIn("QudHUD-v1.0.6.zip", notes)

    def test_first_release_notes(self):
        self.changelog("## 1.0.0\n- First.\n")
        notes = b.release_notes("1.0.0", None)
        self.assertIn("## Changes\n", notes)
        self.assertIn("### 1.0.0", notes)


class ReleaseGate(Temp):
    """--release runs the tests before it tags anything."""

    def test_failing_tests_stop_the_release_before_anything_is_pushed(self):
        version = (REPO / "VERSION").read_text().strip()
        b.DIST = self.tmp
        (self.tmp / f"QudHUD-v{version}.zip").write_bytes(b"zip")
        calls = []

        def git(*args):
            calls.append(args)
            return {"status": "", "tag": "v1.0.1", "rev-parse": "abc123"}.get(args[0], "")

        def failing():
            raise SystemExit("tests failed")

        b.git, b.run_tests = git, failing
        with mock.patch.object(b.shutil, "which", return_value="/usr/bin/gh"):
            with self.assertRaises(SystemExit):
                b.release(version, True)
        self.assertFalse([c for c in calls if c[:2] == ("tag", "-a") or c[0] == "push"],
                         "a tag was created or pushed despite failing tests")

    def test_refuses_without_node(self):
        b.ROOT = self.tmp
        with mock.patch.object(b.shutil, "which", return_value=None):
            with self.assertRaises(SystemExit) as e:
                b.run_tests()
        self.assertIn("node not found", str(e.exception))

    def test_refuses_without_test_dependencies(self):
        b.ROOT = self.tmp
        with mock.patch.object(b.shutil, "which", return_value="/usr/bin/node"):
            with self.assertRaises(SystemExit) as e:
                b.run_tests()
        self.assertIn("npm install", str(e.exception))


class WorkshopVdf(Temp):

    def build_vdf(self):
        b.DIST = self.tmp
        b.WORKSHOP_JSON = self.tmp / "workshop.json"
        b.WORKSHOP_JSON.write_text(json.dumps({"WorkshopId": 3805872225}), encoding="utf-8")
        path = b.build_vdf(Path(r"C:\Users\M\Desktop\qud-hud\dist\QudHUD"), "1.0.9")
        return path.read_text(encoding="utf-8")

    def fields(self, text):
        lines = [l for l in text.splitlines() if l.strip()]
        self.assertEqual(lines[0], '"workshopitem"')
        self.assertEqual((lines[1], lines[-1]), ("{", "}"))
        return lines[2:-1]

    def test_every_value_is_one_quoted_line(self):
        for line in self.fields(self.build_vdf()):
            # two quotes round the key, two round the value, and not one more
            self.assertEqual(line.count('"'), 4, line)

    def test_expected_fields(self):
        keys = [l.split('"')[1] for l in self.fields(self.build_vdf())]
        self.assertEqual(keys, ["appid", "publishedfileid", "contentfolder", "previewfile",
                                "visibility", "title", "changenote"])

    def test_description_is_left_alone(self):
        # it needs line breaks, which this format cannot carry
        self.assertNotIn('"description"', self.build_vdf())

    def test_updates_the_existing_item(self):
        self.assertIn('"publishedfileid"\t\t"3805872225"', self.build_vdf())

    def test_windows_path(self):
        self.assertIn(r'"C:\\Users\\M\\Desktop\\qud-hud\\dist\\QudHUD"', self.build_vdf())

    def test_no_escaped_quotes(self):
        self.assertNotIn('\\"', self.build_vdf())


def unbalanced_braces(src):
    """Brace depth of C# source once strings, chars and comments are skipped. 0 means balanced."""
    depth, instr, inchar, inblock, verbatim = 0, False, False, False, False
    for line in src.split("\n"):
        i = 0
        while i < len(line):
            c = line[i]
            n = line[i + 1] if i + 1 < len(line) else ""
            if inblock:
                if c == "*" and n == "/":
                    inblock = False
                    i += 2
                    continue
            elif instr:
                if verbatim:
                    if c == '"' and n == '"':
                        i += 2
                        continue
                    if c == '"':
                        instr = verbatim = False
                else:
                    if c == "\\":
                        i += 2
                        continue
                    if c == '"':
                        instr = False
            elif inchar:
                if c == "\\":
                    i += 2
                    continue
                if c == "'":
                    inchar = False
            elif c == "/" and n == "/":
                break
            elif c == "/" and n == "*":
                inblock = True
                i += 2
                continue
            elif c == "@" and n == '"':
                instr = verbatim = True
                i += 2
                continue
            elif c == '"':
                instr = True
            elif c == "'":
                inchar = True
            elif c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth < 0:
                    return depth
            i += 1
    return depth


def verbatim_literal(src, marker):
    """The contents of the C# verbatim string that starts right after marker."""
    i, out = src.index(marker) + len(marker), []
    while i < len(src):
        if src[i] == '"':
            if src[i + 1:i + 2] == '"':
                out.append('"')
                i += 2
                continue
            return "".join(out)
        out.append(src[i])
        i += 1
    raise AssertionError("verbatim string never ends")


class Build(Temp):
    """There is no C# compiler here, so these check what can be checked without one."""

    def build(self):
        b.DIST = self.tmp
        with contextlib.redirect_stdout(io.StringIO()):
            folder = b.build()
        return folder, (REPO / "VERSION").read_text().strip()

    def test_page_embeds_byte_for_byte(self):
        # read the literal back exactly as the C# compiler would, so a quote the build forgot to
        # double ends it early and shows up here as a truncated page
        folder, version = self.build()
        cs = (folder / "QudHUD.cs").read_text(encoding="utf-8")
        page = (REPO / "src/hud.html").read_text(encoding="utf-8").replace("__VERSION__", version)
        self.assertEqual(verbatim_literal(cs, 'const string HtmlPage = @"'), page)

    def test_version_is_stamped_everywhere(self):
        folder, version = self.build()
        self.assertEqual(json.loads((folder / "manifest.json").read_text())["Version"], version)
        cs = (folder / "QudHUD.cs").read_text(encoding="utf-8")
        self.assertNotIn("__VERSION__", cs)
        self.assertNotIn("__HTML__", cs)
        self.assertIn(f'Version = "{version}"', cs)

    def test_zip_contents(self):
        _, version = self.build()
        with zipfile.ZipFile(self.tmp / f"QudHUD-v{version}.zip") as z:
            self.assertIsNone(z.testzip())
            names = set(z.namelist())
        for f in ("QudHUD/QudHUD.cs", "QudHUD/manifest.json", "QudHUD/preview.png", "QudHUD/README.md"):
            self.assertIn(f, names)

    def test_template_braces_balance(self):
        self.assertEqual(unbalanced_braces((REPO / "src/QudHUD.template.cs").read_text(encoding="utf-8")), 0)


class LineMapping(unittest.TestCase):
    # the built mod holds the page as many lines where the template holds it as one

    def test_before_the_page_is_unchanged(self):
        self.assertEqual(b.template_line(40, 153, 870), 40)

    def test_inside_the_page_is_reported_as_the_page(self):
        self.assertIsNone(b.template_line(500, 153, 870))

    def test_after_the_page_is_shifted_back(self):
        self.assertEqual(b.template_line(153 + 870 + 5, 153, 870), 158)


def corelib(compiler, kind):
    """The standard library DLLs that go with this compiler."""
    if kind == "dotnet":
        root = Path(compiler[2]).parents[4]
        dirs = sorted(root.glob("packs/Microsoft.NETCore.App.Ref/*/ref/net*"))
        return sorted(dirs[-1].glob("*.dll")) if dirs else []
    here = Path(compiler[0]).parent
    return [here / n for n in ("mscorlib.dll", "System.dll", "System.Core.dll")]


class CSharp(Temp):
    """Compiles the real mod against stand-ins for the game, when a C# compiler is available.

    The stand-ins in tests/stubs mirror how the mod uses the game, so this proves our own code is
    sound. Whether the game's API still matches is what `python build.py --check` is for.
    """

    def setUp(self):
        super().setUp()
        self.compiler, self.kind = b.find_compiler()
        if self.kind not in ("dotnet", "framework"):
            self.skipTest("no C# compiler with a standard library that can be located")
        self.refs = corelib(self.compiler, self.kind)
        if not self.refs:
            self.skipTest("the compiler's standard library was not found")
        self.managed = self.tmp / "Managed"
        self.managed.mkdir()
        for dll in self.refs:
            shutil.copy(dll, self.managed)
        ok, out = b.compile_cs(self.compiler, REPO / "tests/stubs/Game.cs", self.refs,
                               self.managed / "Assembly-CSharp.dll")
        self.assertTrue(ok, out)
        b.DIST = self.tmp / "dist"
        with contextlib.redirect_stdout(io.StringIO()):
            b.build()

    def check(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            try:
                b.check(self.managed)
                return True, output.getvalue()
            except SystemExit:
                return False, output.getvalue()

    def test_mod_compiles(self):
        ok, output = self.check()
        self.assertTrue(ok, output)

    def test_mod_is_valid_csharp_5(self):
        # the compiler every Windows install already has stops at C# 5, and --check relies on it
        extra = ["-langversion:5"] if self.kind == "dotnet" else []
        ok, output = b.compile_cs(self.compiler, b.DIST / "QudHUD/QudHUD.cs",
                                  sorted(self.managed.glob("*.dll")), self.tmp / "v5.dll", extra)
        self.assertTrue(ok, output)

    def test_an_error_is_reported_at_its_template_line(self):
        needle = "Perception.SelfExact(p)"
        template = (REPO / "src/QudHUD.template.cs").read_text(encoding="utf-8").splitlines()
        line = next(i for i, l in enumerate(template, 1) if needle in l)
        built = b.DIST / "QudHUD/QudHUD.cs"
        built.write_text(built.read_text(encoding="utf-8").replace(needle, "Perception.SelfExcat(p)"),
                         encoding="utf-8")
        ok, output = self.check()
        self.assertFalse(ok)
        self.assertIn(f"src/QudHUD.template.cs({line},", output)
        self.assertIn("SelfExcat", output)


class Text(unittest.TestCase):

    def test_no_long_dashes(self):
        try:
            files = subprocess.run(["git", "ls-files"], cwd=REPO, capture_output=True,
                                   text=True, check=True).stdout.split()
        except (OSError, subprocess.CalledProcessError):
            self.skipTest("not a git checkout")
        dashes = ("\u2014", "\u2013")
        for f in files:
            if f.endswith((".png", ".zip")):
                continue
            text = (REPO / f).read_text(encoding="utf-8", errors="ignore")
            for n, line in enumerate(text.splitlines(), 1):
                self.assertFalse(any(d in line for d in dashes), f"{f}:{n} has a long dash")


if __name__ == "__main__":
    unittest.main(verbosity=1)
