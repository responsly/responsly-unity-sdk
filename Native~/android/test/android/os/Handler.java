package android.os;
public class Handler { public static int posted; public Handler(Looper l) {} public boolean postDelayed(Runnable r, long ms) { posted++; r.run(); return true; } public boolean post(Runnable r) { r.run(); return true; } }
