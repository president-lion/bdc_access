import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;
import java.io.*;

public class DisasmAt extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        String outPath = args[0];
        int before = Integer.parseInt(args[1]);
        int after = Integer.parseInt(args[2]);
        long base = currentProgram.getImageBase().getOffset();
        PrintWriter w = new PrintWriter(new OutputStreamWriter(new FileOutputStream(outPath), "UTF-8"));
        Listing lst = currentProgram.getListing();

        for (int i = 3; i < args.length; i++) {
            long rva = Long.parseLong(args[i], 16);
            Address at = currentProgram.getImageBase().add(rva);
            w.printf("%n===== RVA %08x (VA %s) =====%n", rva, at);
            // walk backwards
            Instruction ins = lst.getInstructionContaining(at);
            if (ins == null) { w.println("  <no instruction>"); continue; }
            Instruction cur = ins;
            for (int b = 0; b < before && cur != null; b++) {
                Instruction p = cur.getPrevious();
                if (p == null) break;
                cur = p;
            }
            for (int n = 0; n < before + after + 1 && cur != null; n++) {
                long r = cur.getAddress().getOffset() - base;
                String mark = cur.getAddress().equals(ins.getAddress()) ? " <<<" : "";
                w.printf("  %08x  %-40s%s%n", r, cur.toString(), mark);
                cur = cur.getNext();
            }
        }
        w.close();
        println("DISASM DONE -> " + outPath);
    }
}
