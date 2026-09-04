import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;
import java.io.*;

public class DecompAt extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        String outPath = args[0];
        DecompInterface di = new DecompInterface();
        di.openProgram(currentProgram);
        PrintWriter w = new PrintWriter(new OutputStreamWriter(new FileOutputStream(outPath), "UTF-8"));

        for (int i = 1; i < args.length; i++) {
            long va = Long.parseLong(args[i], 16);
            Address at = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(va);
            Function f = getFunctionContaining(at);
            w.printf("%n/* ================= VA %08x ================= */%n", va);
            if (f == null) {
                f = createFunction(at, null);
                if (f == null) { w.println("/* no function */"); continue; }
            }
            w.printf("/* entry %s  name %s */%n", f.getEntryPoint(), f.getName());
            DecompileResults res = di.decompileFunction(f, 180, monitor);
            if (res.decompileCompleted()) w.println(res.getDecompiledFunction().getC());
            else w.println("/* decompile failed: " + res.getErrorMessage() + " */");
        }
        w.close();
        di.dispose();
        println("DECOMP DONE -> " + outPath);
    }
}
