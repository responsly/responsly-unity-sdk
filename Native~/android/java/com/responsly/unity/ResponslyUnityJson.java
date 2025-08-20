package com.responsly.unity;

import java.util.Map;

/**
 * Writes the JSON documents that go to the C# side. Hand-written so the output is the same on
 * every Android version: compact, no escaped slashes, whole numbers without a fraction.
 */
final class ResponslyUnityJson {
    private static final char[] HEX = "0123456789abcdef".toCharArray();

    private ResponslyUnityJson() {
    }

    /** Serialises a String, Number, Boolean, Map, Iterable or null; anything else as its text. */
    static String write(Object value) {
        StringBuilder out = new StringBuilder(128);
        append(out, value);
        return out.toString();
    }

    private static void append(StringBuilder out, Object value) {
        if (value == null) {
            out.append("null");
        } else if (value instanceof String) {
            appendString(out, (String) value);
        } else if (value instanceof Boolean) {
            out.append(((Boolean) value).booleanValue() ? "true" : "false");
        } else if (value instanceof Number) {
            appendNumber(out, (Number) value);
        } else if (value instanceof Map) {
            out.append('{');
            boolean first = true;
            for (Map.Entry<?, ?> entry : ((Map<?, ?>) value).entrySet()) {
                if (!first) {
                    out.append(',');
                }
                first = false;
                appendString(out, String.valueOf(entry.getKey()));
                out.append(':');
                append(out, entry.getValue());
            }
            out.append('}');
        } else if (value instanceof Iterable) {
            out.append('[');
            boolean first = true;
            for (Object item : (Iterable<?>) value) {
                if (!first) {
                    out.append(',');
                }
                first = false;
                append(out, item);
            }
            out.append(']');
        } else {
            appendString(out, value.toString());
        }
    }

    private static void appendNumber(StringBuilder out, Number value) {
        if (value instanceof Double || value instanceof Float) {
            double number = value.doubleValue();
            if (Double.isNaN(number) || Double.isInfinite(number)) {
                // JSON has no such numbers.
                out.append("null");
            } else if (number == Math.rint(number) && Math.abs(number) < 1.0e15) {
                out.append((long) number);
            } else {
                out.append(value.toString());
            }
        } else {
            out.append(value.toString());
        }
    }

    private static void appendString(StringBuilder out, String text) {
        out.append('"');
        for (int index = 0; index < text.length(); index++) {
            char c = text.charAt(index);
            switch (c) {
                case '"':
                    out.append("\\\"");
                    break;
                case '\\':
                    out.append("\\\\");
                    break;
                case '\n':
                    out.append("\\n");
                    break;
                case '\r':
                    out.append("\\r");
                    break;
                case '\t':
                    out.append("\\t");
                    break;
                case '\b':
                    out.append("\\b");
                    break;
                case '\f':
                    out.append("\\f");
                    break;
                default:
                    if (c < 0x20) {
                        out.append("\\u00");
                        out.append(HEX[(c >> 4) & 0xf]);
                        out.append(HEX[c & 0xf]);
                    } else {
                        out.append(c);
                    }
                    break;
            }
        }
        out.append('"');
    }
}
