CREATE (w:Example {name:'가다', description:'아침 일찍 집을 나선 아이는 할머니 댁에 가려고 마을의 오래된 다리를 건넜다. 강가에서 잠깐 발걸음을 멈춘 아이는 하늘에 떠 있는 구름을 바라보다가 다시 길을 따라 걸어갔다. 해가 질 무렵 대문 앞에서 기다리던 할머니에게 오늘 만난 사람들의 이야기를 처음부터 끝까지 들려주었다.', examples:['학교에 가다','걸어서 가다','할머니 댁에 가다','강을 따라 천천히 가다']})
CREATE (b:Example {name:'도착하다'})
CREATE (w)-[:RELATED {order:1}]->(b), (w)-[:RELATED {order:2}]->(b), (b)-[:RETURNS_TO]->(w), (w)-[:REPEATS]->(w);
