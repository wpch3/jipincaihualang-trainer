#!/usr/bin/env python3
"""Dump .NET method bodies + token names from a managed assembly.

Usage:
  python tools/il_dump.py <dll> [--fn <substr>] [--type <substr>] [--no-body] [--max N]
"""
import sys, struct
import pefile, dnfile
from dncil.cil.body.reader import read_method_body_from_bytes

_token_tables = {
    0x01:'TypeDef',0x02:'TypeRef',0x03:'TypeSpec',0x04:'TypeDef',
    0x06:'MethodDef',0x08:'Field',0x09:'MethodDef',0x0a:'MemberRef',
    0x0b:'MemberRef',0x1c:'MethodSpec',0x70:'String',0x24:'Sig'
}

class Dumper:
    def __init__(self, path):
        self.path = path
        self.pe = pefile.PE(path, fast_load=True)
        self.dn = dnfile.dnPE(path)

    def rva_to_off(self, rva):
        for sec in self.pe.sections:
            if sec.VirtualAddress <= rva < sec.VirtualAddress + max(sec.Misc_VirtualSize, sec.SizeOfRawData):
                return sec.PointerToRawData + (rva - sec.VirtualAddress)
        return None

    def raw_method_bytes(self, rva, limit=0x20000):
        off = self.rva_to_off(rva)
        if off is None:
            return b''
        # read a generous chunk; dncil will consume one header/code/sections
        with open(self.path,'rb') as f:
            f.seek(off)
            return f.read(limit)

    def resolve_token(self, tok):
        table = tok >> 24; idx = (tok & 0xffffff)
        dn = self.dn
        try:
            if table == 0x70:
                try: return repr(dn.net.user_strings.get(idx-1))
                except Exception: return f"str:{idx:#x}"
            if table == 0x02:
                r = dn.net.mdtables.TypeRef[idx-1]
                return f"{r.TypeNamespace}.{r.TypeName}"
            if table == 0x01:
                r = dn.net.mdtables.TypeDef[idx-1]
                return f"{r.TypeNamespace}.{r.TypeName}"
            if table == 0x04:
                r = dn.net.mdtables.TypeDef[idx-1]
                return f"{r.TypeNamespace}.{r.TypeName}"
            if table == 0x06:
                r = dn.net.mdtables.MethodDef[idx-1]
                return f"{r.Name}"
            if table == 0x0a:
                r = dn.net.mdtables.MemberRef[idx-1]
                return f"{r.Name}"
            if table == 0x08:
                r = dn.net.mdtables.Field[idx-1]
                return f"{r.Name}"
            if table == 0x1b:
                r = dn.net.mdtables.MethodDef[idx-1]
                return f"{r.Name}"
            if table == 0x1c:
                r = dn.net.mdtables.MethodSpec[idx-1]
                return f"MethodSpec:{idx:#x}"
            return f"tok:{tok:#010x}"
        except Exception as e:
            return f"tok:{tok:#010x}({e})"

    def fmt_instr(self, ins):
        mn = ins.mnemonic
        op = ins.operand
        if op is None:
            return mn
        val = None
        try:
            if hasattr(op,'value'):
                val = op.value
            elif isinstance(op,int):
                val = op
            elif hasattr(op,'token'):
                val = op.token
            else:
                val = repr(op)
        except Exception:
            val = repr(op)
        # branch target
        if ins.is_br() or ins.is_cond_br() or ins.is_leave():
            try:
                disp = val if isinstance(val,int) else struct.unpack('<i', ins.operand_bytes)[0]
                target = ins.offset + ins.size + disp
                return f"{mn} IL_{target:04x}"
            except Exception:
                pass
        if mn in ('ldstr','call','callvirt','newobj','ldfld','stfld','ldsfld','stsfld','ldtoken','box','unbox','unbox.any','castclass','isinst','newarr','ldelema','initobj','ldobj','stobj','constrained.','sizeof','mkrefany','refanyval','cpobj'):
            toks = []
            if hasattr(op,'value'):
                toks.append(f"0x{op.value & 0xffffffff:X}")
            elif str(op).startswith('0x'):
                toks.append(str(op))
            elif isinstance(op,int):
                toks.append(f"0x{op:X}")
            if toks:
                return f"{mn} {toks[0]} `{self.resolve_token(int(toks[0],16) if toks[0].startswith('0x') else int(toks[0]))}`"
            return f"{mn} {val}"
        if mn in ('switch',):
            try:
                return mn + ' ' + repr(op)
            except Exception:
                return mn
        return f"{mn} {val}"

    def dump_method(self, clsname, m):
        rva = getattr(m,'Rva',0)
        if not rva:
            return None
        raw = self.raw_method_bytes(rva)
        try:
            body = read_method_body_from_bytes(raw)
        except Exception as e:
            return [(f"<parse error {e}>", None)]
        out = []
        for ins in body.instructions:
            out.append((ins.offset, self.fmt_instr(ins)))
        return out

    def methods(self, typ, fn, no_body, maxn):
        count=0
        for ti,tr in enumerate(self.dn.net.mdtables.TypeDef):
            cls = f"{tr.TypeNamespace}.{tr.TypeName}" if tr.TypeNamespace else f"{tr.TypeName}"
            if typ and typ not in cls:
                continue
            meths = getattr(tr,'MethodList',[])
            for mref in meths:
                m = mref.row if hasattr(mref,'row') else mref
                name = str(m.Name)
                if fn and fn not in name:
                    continue
                rva = getattr(m,'Rva',0)
                print(f"### {cls} :: {name} RVA={rva and hex(rva) or '0'}")
                if no_body or not rva:
                    continue
                ins = self.dump_method(cls, m)
                if ins is None:
                    continue
                for off,txt in ins:
                    print(f"  {off:04X}: {txt}")
                count += 1
                print()
                if maxn and count>=maxn:
                    return

def main():
    args = sys.argv[1:]
    path = args[0] if args else 'FlowerPicker.dll'
    fn=None; typ=None; no_body=False; maxn=None
    i=1
    while i<len(args):
        if args[i]=='--fn': fn=args[i+1]; i+=2
        elif args[i]=='--type': typ=args[i+1]; i+=2
        elif args[i]=='--no-body': no_body=True; i+=1
        elif args[i]=='--max': maxn=int(args[i+1]); i+=2
        else: i+=1
    d=Dumper(path)
    d.methods(typ, fn, no_body, maxn)

if __name__=='__main__':
    main()
