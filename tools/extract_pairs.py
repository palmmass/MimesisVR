import re, sys, json, glob
CYR=re.compile(r'[А-Яа-яЁё]')
def lits(s):
    out=[]; i=0; n=len(s)
    while i<n:
        c=s[i]
        if c=='/' and s.startswith('//',i): i=s.find('\n',i); i=n if i<0 else i; continue
        if c=='/' and s.startswith('/*',i): i=s.find('*/',i)+2; continue
        if c=="'" :
            j=i+1
            while s[j]!="'": j+= 2 if s[j]=='\\' else 1
            i=j+1; continue
        if c=='"' or (c in '$@' and i+1<n and s[i+1] in '"$@'):
            j=i; pre=''
            while s[j] in '$@': pre+=s[j]; j+=1
            assert s[j]=='"'; j+=1; buf=''
            verb='@' in pre
            while True:
                ch=s[j]
                if verb and ch=='"' and s[j+1:j+2]=='"': buf+='"'; j+=2; continue
                if not verb and ch=='\\': 
                    e=s[j+1]; buf+={'n':'\n','t':'\t','"':'"','\\':'\\'}.get(e,'\\'+e); j+=2; continue
                if ch=='"': break
                buf+=ch; j+=1
            out.append((i,j+1,buf,'$' in pre)); i=j+1; continue
        i+=1
    return out
pairs=[]; unmatched=[]
for f in sorted(glob.glob(sys.argv[1]+'/**/*.cs',recursive=True)):
    s=open(f,encoding='utf-8').read(); L=lits(s); used=set()
    # arrays: new[] { en... }, new[] { ru... }
    for m in re.finditer(r'new\[\]\s*\{([^{}]*)\}\s*,\s*new\[\]\s*\{([^{}]*)\}',s):
        a=[x for x in L if m.start(1)<=x[0]<m.end(1)]; b=[x for x in L if m.start(2)<=x[0]<m.end(2)]
        if b and any(CYR.search(x[2]) for x in b) and len(a)==len(b):
            for x,y in zip(a,b): pairs.append((x[2],y[2],f,x[3] or y[3])); used.add(x[0]); used.add(y[0])
    for k,(st,en,txt,interp) in enumerate(L):
        if st in used or not CYR.search(txt): continue
        prev=L[k-1] if k>0 else None; nxt=L[k+1] if k+1<len(L) else None
        between_prev=s[prev[1]:st] if prev else ''
        between_next=s[en:nxt[0]] if nxt else ''
        if nxt and re.fullmatch(r'\s*:\s*',between_next) and not CYR.search(nxt[2]):
            pairs.append((nxt[2],txt,f,interp or nxt[3])); used.add(st); used.add(nxt[0]); continue
        if prev and prev[0] not in used and re.fullmatch(r'\s*,\s*(\w+\s*=\s*)?',between_prev) and not CYR.search(prev[2]):
            pairs.append((prev[2],txt,f,interp or prev[3])); used.add(st); used.add(prev[0]); continue
        unmatched.append((f,s.count('\n',0,st)+1,txt[:80]))
json.dump({'pairs':pairs,'unmatched':unmatched},open(sys.argv[2],'w',encoding='utf-8'),ensure_ascii=False,indent=0)
print(len(pairs),'pairs;',len(unmatched),'unmatched;',sum(1 for p in pairs if p[3]),'interpolated')
for u in unmatched: print('U',u)
for p in pairs:
    if p[3]: print('I',repr(p[0])[:90])
