package android.util;
public class Log { public static int e(String t, String m) { System.out.println("LOG.e " + m); return 0; } public static int e(String t, String m, Throwable x) { System.out.println("LOG.e " + m + " " + x); return 0; } }
