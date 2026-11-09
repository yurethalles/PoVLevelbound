# Changelog

## 1.2.0 — 9 de novembro de 2026

### Alterado

- O nível é obtido pela mesma via do PoVMonsterLevels (`PassiveTree.Progress.Peek`) com fallback seguro para `pt.level`.
- Os níveis padrão agora acompanham a curva recomendada do Path of Valheiman; Deep North usa 200, o máximo padrão alcançável do PoV.
- Ocean recebeu uma regra de nível própria, alinhada a Mistlands.
- Regras YAML são geradas automaticamente uma vez na primeira execução.

### Corrigido

- O mod exige Path of Valheiman, impedindo uma instalação que deixaria jogadores no nível zero.
- A RPC de bloqueio usa um nome exclusivo e não conflita com o Levelbound antigo.

## 1.1.0 — 8 de novembro de 2026

### Adicionado

- Compatibilidade opcional com **Path of Valheiman 5.3.6** nos tooltips de itens.
- Compatibilidade com **Marketplace and Server NPCs Revamped 10.0.8**:
  - o painel de detalhes mostra o requisito de nível no topo;
  - quando o PoV está instalado, o painel preserva raridade, ilvl, implícitos e afixos;
- Proteção de compra no Marketplace:
  - o botão de compra é desabilitado para itens cujo nível ainda não foi atingido;
  - a tentativa de compra avisa o jogador antes de remover moedas;
  - o servidor valida o nível antes de processar o anúncio e entregar o item.

### Alterado

- Os requisitos exibidos em item, construção, altar e Marketplace agora acompanham o idioma ativo do jogo.
- O requisito em tooltips personalizados do PoV é exibido fora da citação de lore.

### Corrigido

- Requisitos ausentes em tooltips de inventário e crafting quando o PoV substitui a UI vanilla.
- Requisito ausente no Marketplace para itens temporários, preservando o prefab original do anúncio.
