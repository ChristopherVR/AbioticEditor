#pragma once
// A deliberately minimal JSON reader/writer for the message shapes this protocol actually sends:
// an object whose values are strings, numbers, booleans, null, one level of nested flat object
// (the "payload"), or a flat array of such objects (e.g. skills.get's per-skill rows). The wire
// protocol (see docs/reference/live-editing-protocol.md) never needs deeper nesting than that,
// so this avoids vendoring a general JSON library for a handful of known shapes.
//
// This mirrors AbioticEditor.Core.LiveEditing.TcpLiveGameChannel on the .NET side: that class
// serializes with System.Text.Json using camelCase property names, so every key this reads or
// writes is camelCase ("id", "cmd", "token", "payload", "ok", "result", "error"), never PascalCase.

#include <map>
#include <memory>
#include <sstream>
#include <string>
#include <variant>
#include <vector>

namespace LiveAgent
{
    class JsonValue;
    using JsonObject = std::map<std::string, JsonValue>;
    using JsonArray = std::vector<JsonValue>;

    class JsonValue
    {
    public:
        JsonValue() : m_data(nullptr) {}
        JsonValue(std::string value) : m_data(std::move(value)) {}
        JsonValue(const char* value) : m_data(std::string(value)) {}
        JsonValue(double value) : m_data(value) {}
        JsonValue(int value) : m_data(static_cast<double>(value)) {}
        JsonValue(bool value) : m_data(value) {}
        JsonValue(JsonObject value) : m_data(std::make_shared<JsonObject>(std::move(value))) {}
        JsonValue(JsonArray value) : m_data(std::make_shared<JsonArray>(std::move(value))) {}

        bool IsNull() const { return std::holds_alternative<std::nullptr_t>(m_data); }
        bool IsObject() const { return std::holds_alternative<std::shared_ptr<JsonObject>>(m_data); }
        bool IsArray() const { return std::holds_alternative<std::shared_ptr<JsonArray>>(m_data); }

        std::string AsString(const std::string& fallback = {}) const
        {
            if (auto* value = std::get_if<std::string>(&m_data)) return *value;
            return fallback;
        }

        double AsNumber(double fallback = 0.0) const
        {
            if (auto* value = std::get_if<double>(&m_data)) return *value;
            return fallback;
        }

        bool AsBool(bool fallback = false) const
        {
            if (auto* value = std::get_if<bool>(&m_data)) return *value;
            return fallback;
        }

        const JsonObject* AsObject() const
        {
            if (auto* value = std::get_if<std::shared_ptr<JsonObject>>(&m_data)) return value->get();
            return nullptr;
        }

        const JsonArray* AsArray() const
        {
            if (auto* value = std::get_if<std::shared_ptr<JsonArray>>(&m_data)) return value->get();
            return nullptr;
        }

        // Writes this value's JSON text (recursively for an object) onto `out`.
        void Write(std::ostringstream& out) const
        {
            if (IsNull()) { out << "null"; return; }
            if (auto* s = std::get_if<std::string>(&m_data)) { WriteEscaped(out, *s); return; }
            if (auto* n = std::get_if<double>(&m_data))
            {
                // Every value on this wire is either an integer-valued stat or a double; printing
                // without a trailing ".0" for whole numbers keeps ints round-tripping as ints on
                // the C# side (System.Text.Json reads "42" into an int fine, "42.0" needs a double).
                if (*n == static_cast<long long>(*n)) out << static_cast<long long>(*n);
                else out << *n;
                return;
            }
            if (auto* b = std::get_if<bool>(&m_data)) { out << (*b ? "true" : "false"); return; }
            if (auto* o = std::get_if<std::shared_ptr<JsonObject>>(&m_data))
            {
                out << '{';
                bool first = true;
                for (const auto& [key, value] : **o)
                {
                    if (!first) out << ',';
                    first = false;
                    WriteEscaped(out, key);
                    out << ':';
                    value.Write(out);
                }
                out << '}';
                return;
            }
            if (auto* a = std::get_if<std::shared_ptr<JsonArray>>(&m_data))
            {
                out << '[';
                bool first = true;
                for (const auto& value : **a)
                {
                    if (!first) out << ',';
                    first = false;
                    value.Write(out);
                }
                out << ']';
                return;
            }
        }

        static void WriteEscaped(std::ostringstream& out, const std::string& text)
        {
            out << '"';
            for (char c : text)
            {
                switch (c)
                {
                case '"': out << "\\\""; break;
                case '\\': out << "\\\\"; break;
                case '\n': out << "\\n"; break;
                case '\r': out << "\\r"; break;
                case '\t': out << "\\t"; break;
                default: out << c;
                }
            }
            out << '"';
        }

    private:
        std::variant<std::nullptr_t, std::string, double, bool,
            std::shared_ptr<JsonObject>, std::shared_ptr<JsonArray>> m_data;
    };

    // A small recursive-descent parser for exactly the shapes this protocol sends: an object or
    // array of string/number/bool/null/object/array values, one level deep of nesting. Throws
    // std::runtime_error on anything it does not understand (a malformed line) - the caller
    // treats that as "the request could not be read" and answers with ok:false rather than
    // crashing the game process.
    class JsonParser
    {
    public:
        explicit JsonParser(const std::string& text) : m_text(text), m_pos(0) {}

        JsonValue ParseValue()
        {
            SkipWhitespace();
            if (m_pos >= m_text.size()) throw std::runtime_error("unexpected end of JSON");
            char c = m_text[m_pos];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return JsonValue(ParseString());
            if (c == 't' && m_text.compare(m_pos, 4, "true") == 0) { m_pos += 4; return JsonValue(true); }
            if (c == 'f' && m_text.compare(m_pos, 5, "false") == 0) { m_pos += 5; return JsonValue(false); }
            if (c == 'n' && m_text.compare(m_pos, 4, "null") == 0) { m_pos += 4; return JsonValue(); }
            return ParseNumber();
        }

    private:
        const std::string& m_text;
        size_t m_pos;

        void SkipWhitespace() { while (m_pos < m_text.size() && std::isspace((unsigned char)m_text[m_pos])) ++m_pos; }

        JsonValue ParseObject()
        {
            JsonObject object;
            ++m_pos; // '{'
            SkipWhitespace();
            if (m_pos < m_text.size() && m_text[m_pos] == '}') { ++m_pos; return JsonValue(std::move(object)); }
            while (true)
            {
                SkipWhitespace();
                auto key = ParseString();
                SkipWhitespace();
                if (m_pos >= m_text.size() || m_text[m_pos] != ':') throw std::runtime_error("expected ':'");
                ++m_pos;
                object.emplace(std::move(key), ParseValue());
                SkipWhitespace();
                if (m_pos < m_text.size() && m_text[m_pos] == ',') { ++m_pos; continue; }
                if (m_pos < m_text.size() && m_text[m_pos] == '}') { ++m_pos; break; }
                throw std::runtime_error("expected ',' or '}'");
            }
            return JsonValue(std::move(object));
        }

        JsonValue ParseArray()
        {
            JsonArray array;
            ++m_pos; // '['
            SkipWhitespace();
            if (m_pos < m_text.size() && m_text[m_pos] == ']') { ++m_pos; return JsonValue(std::move(array)); }
            while (true)
            {
                array.push_back(ParseValue());
                SkipWhitespace();
                if (m_pos < m_text.size() && m_text[m_pos] == ',') { ++m_pos; continue; }
                if (m_pos < m_text.size() && m_text[m_pos] == ']') { ++m_pos; break; }
                throw std::runtime_error("expected ',' or ']'");
            }
            return JsonValue(std::move(array));
        }

        std::string ParseString()
        {
            if (m_text[m_pos] != '"') throw std::runtime_error("expected '\"'");
            ++m_pos;
            std::string result;
            while (m_pos < m_text.size() && m_text[m_pos] != '"')
            {
                char c = m_text[m_pos++];
                if (c == '\\' && m_pos < m_text.size())
                {
                    char escaped = m_text[m_pos++];
                    switch (escaped)
                    {
                    case 'n': result += '\n'; break;
                    case 'r': result += '\r'; break;
                    case 't': result += '\t'; break;
                    default: result += escaped;
                    }
                }
                else result += c;
            }
            if (m_pos >= m_text.size()) throw std::runtime_error("unterminated string");
            ++m_pos; // closing '"'
            return result;
        }

        JsonValue ParseNumber()
        {
            size_t start = m_pos;
            while (m_pos < m_text.size() && (std::isdigit((unsigned char)m_text[m_pos])
                || m_text[m_pos] == '-' || m_text[m_pos] == '+' || m_text[m_pos] == '.'
                || m_text[m_pos] == 'e' || m_text[m_pos] == 'E'))
            {
                ++m_pos;
            }
            if (m_pos == start) throw std::runtime_error("expected a value");
            return JsonValue(std::stod(m_text.substr(start, m_pos - start)));
        }
    };

    inline JsonValue ParseLine(const std::string& line)
    {
        JsonParser parser(line);
        return parser.ParseValue();
    }

    inline std::string ToLine(const JsonValue& value)
    {
        std::ostringstream out;
        value.Write(out);
        return out.str();
    }
}
